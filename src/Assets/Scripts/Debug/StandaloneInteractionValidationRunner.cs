using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
[DefaultExecutionOrder(-1000)]
public sealed class StandaloneInteractionValidationRunner : MonoBehaviour
{
    private const string JobArgument = "-standaloneValidationJob";
    private const string ResultDirectoryArgument = "-standaloneValidationResultDir";

    [Serializable]
    private sealed class ValidationJob
    {
        public string runId;
        public float startupDelaySeconds = 2f;
        public float timeoutSeconds = 300f;
        public bool quitOnComplete = true;
        public ValidationStep[] steps = Array.Empty<ValidationStep>();
    }

    [Serializable]
    private sealed class ValidationStep
    {
        public string command;
        public string[] arguments = Array.Empty<string>();
        public float delayBeforeSeconds;
        public float delayAfterSeconds = 0.25f;
        public string expectContains;
        public string rejectContains;
        public bool continueOnFailure;
    }

    [Serializable]
    private sealed class ValidationRunResult
    {
        public string runId;
        public string jobPath;
        public string resultDirectory;
        public string startedUtc;
        public string finishedUtc;
        public bool success;
        public string error;
        public ValidationStepResult[] steps = Array.Empty<ValidationStepResult>();
    }

    [Serializable]
    private sealed class ValidationStepResult
    {
        public int index;
        public string command;
        public string[] arguments;
        public string startedUtc;
        public string finishedUtc;
        public bool success;
        public string output;
        public string error;
    }

    private static StandaloneInteractionValidationRunner _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        string jobPath = ReadArgument(args, JobArgument);
        if (string.IsNullOrWhiteSpace(jobPath) || _instance != null)
            return;

        var host = new GameObject("[StandaloneInteractionValidationRunner]");
        DontDestroyOnLoad(host);
        _instance = host.AddComponent<StandaloneInteractionValidationRunner>();
        _instance.StartCoroutine(_instance.Run(jobPath, ReadArgument(args, ResultDirectoryArgument)));
    }

    private IEnumerator Run(string rawJobPath, string rawResultDirectory)
    {
        Application.runInBackground = true;

        string jobPath = Path.GetFullPath(rawJobPath);
        ValidationRunResult runResult = null;
        ValidationJob job = null;
        Exception initializationError = null;
        var stepResults = new List<ValidationStepResult>();
        float startedRealtime = Time.realtimeSinceStartup;

        try
        {
            if (!File.Exists(jobPath))
                throw new FileNotFoundException("Standalone validation job was not found.", jobPath);

            job = JsonUtility.FromJson<ValidationJob>(File.ReadAllText(jobPath));
            if (job == null)
                throw new InvalidDataException("Standalone validation job JSON could not be parsed.");

            string runId = SanitizePathSegment(string.IsNullOrWhiteSpace(job.runId)
                ? DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture)
                : job.runId);
            string resultDirectory = string.IsNullOrWhiteSpace(rawResultDirectory)
                ? Path.Combine(Path.GetDirectoryName(jobPath) ?? Application.dataPath, "Results", runId)
                : Path.GetFullPath(rawResultDirectory);

            Directory.CreateDirectory(resultDirectory);
            runResult = new ValidationRunResult
            {
                runId = runId,
                jobPath = jobPath,
                resultDirectory = resultDirectory,
                startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };
            WriteResult(runResult, stepResults);
        }
        catch (Exception ex)
        {
            string fallbackDirectory = string.IsNullOrWhiteSpace(rawResultDirectory)
                ? Path.Combine(Path.GetDirectoryName(jobPath) ?? Application.dataPath, "Results", "failed-start")
                : Path.GetFullPath(rawResultDirectory);
            Directory.CreateDirectory(fallbackDirectory);
            runResult = new ValidationRunResult
            {
                runId = "failed-start",
                jobPath = jobPath,
                resultDirectory = fallbackDirectory,
                startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                finishedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                success = false,
                error = ex.ToString()
            };
            WriteResult(runResult, stepResults);
            Debug.LogError($"[StandaloneValidation] FAIL {ex}");
            initializationError = ex;
        }

        if (initializationError != null)
        {
            if (!Application.isEditor)
                Application.Quit(2);
            yield break;
        }

        Debug.Log($"[StandaloneValidation] START runId={runResult.runId} job={jobPath}");

        if (job.startupDelaySeconds > 0f)
            yield return new WaitForSecondsRealtime(job.startupDelaySeconds);

        bool aborted = false;
        ValidationStep[] steps = job.steps ?? Array.Empty<ValidationStep>();
        for (int i = 0; i < steps.Length; i++)
        {
            if (job.timeoutSeconds > 0f && Time.realtimeSinceStartup - startedRealtime > job.timeoutSeconds)
            {
                runResult.error = $"Validation run exceeded {job.timeoutSeconds:0.###} seconds before step {i}.";
                aborted = true;
                break;
            }

            ValidationStep step = steps[i] ?? new ValidationStep();
            if (step.delayBeforeSeconds > 0f)
                yield return new WaitForSecondsRealtime(step.delayBeforeSeconds);

            var stepResult = new ValidationStepResult
            {
                index = i,
                command = step.command ?? string.Empty,
                arguments = step.arguments ?? Array.Empty<string>(),
                startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };

            try
            {
                stepResult.output = InvokeCommand(step.command, stepResult.arguments);
                ValidateOutput(step, stepResult.output);
                stepResult.success = true;
            }
            catch (Exception ex)
            {
                Exception actual = ex is TargetInvocationException invocation && invocation.InnerException != null
                    ? invocation.InnerException
                    : ex;
                stepResult.success = false;
                stepResult.error = actual.ToString();
            }

            stepResult.finishedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            stepResults.Add(stepResult);
            WriteResult(runResult, stepResults);
            Debug.Log($"[StandaloneValidation] STEP index={i} command={stepResult.command} success={stepResult.success} output={stepResult.output}");

            if (!stepResult.success && !step.continueOnFailure)
            {
                runResult.error = $"Validation step {i} ({stepResult.command}) failed: {stepResult.error}";
                aborted = true;
                break;
            }

            if (step.delayAfterSeconds > 0f)
                yield return new WaitForSecondsRealtime(step.delayAfterSeconds);
        }

        bool success = !aborted && stepResults.All(result => result.success);
        int exitCode = success ? 0 : 2;
        runResult.success = success;
        runResult.finishedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        WriteResult(runResult, stepResults);
        Debug.Log($"[StandaloneValidation] END runId={runResult.runId} success={runResult.success} result={runResult.resultDirectory}");

        if (!Application.isEditor && job.quitOnComplete)
            Application.Quit(exitCode);
    }

    private static string InvokeCommand(string command, string[] arguments)
    {
        if (string.IsNullOrWhiteSpace(command) || string.Equals(command, "Wait", StringComparison.OrdinalIgnoreCase))
            return "WAIT_COMPLETE";

        string[] supplied = arguments ?? Array.Empty<string>();
        MethodInfo method = typeof(DebugRemoteControl)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(candidate => string.Equals(candidate.Name, command, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.GetParameters().Length == supplied.Length ? 0 : 1)
            .FirstOrDefault(candidate => CanInvoke(candidate, supplied.Length));

        if (method == null)
            throw new MissingMethodException(typeof(DebugRemoteControl).FullName, command);

        ParameterInfo[] parameters = method.GetParameters();
        var converted = new object[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
            converted[i] = i < supplied.Length ? ConvertArgument(supplied[i], parameters[i].ParameterType) : parameters[i].DefaultValue;

        object value = method.Invoke(null, converted);
        return value?.ToString() ?? string.Empty;
    }

    private static bool CanInvoke(MethodInfo method, int suppliedCount)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (suppliedCount > parameters.Length)
            return false;

        for (int i = suppliedCount; i < parameters.Length; i++)
        {
            if (!parameters[i].IsOptional)
                return false;
        }

        return true;
    }

    private static object ConvertArgument(string value, Type targetType)
    {
        Type actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (actualType == typeof(string))
            return value;
        if (actualType == typeof(bool))
            return bool.Parse(value);
        if (actualType.IsEnum)
            return Enum.Parse(actualType, value, true);
        return Convert.ChangeType(value, actualType, CultureInfo.InvariantCulture);
    }

    private static void ValidateOutput(ValidationStep step, string output)
    {
        string actual = output ?? string.Empty;
        if (!string.IsNullOrEmpty(step.expectContains) && actual.IndexOf(step.expectContains, StringComparison.OrdinalIgnoreCase) < 0)
            throw new InvalidOperationException($"Output did not contain expected text '{step.expectContains}'. Actual: {actual}");
        if (!string.IsNullOrEmpty(step.rejectContains) && actual.IndexOf(step.rejectContains, StringComparison.OrdinalIgnoreCase) >= 0)
            throw new InvalidOperationException($"Output contained rejected text '{step.rejectContains}'. Actual: {actual}");
    }

    private static string ReadArgument(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static string SanitizePathSegment(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private static void WriteResult(ValidationRunResult runResult, List<ValidationStepResult> stepResults)
    {
        if (runResult == null || string.IsNullOrWhiteSpace(runResult.resultDirectory))
            return;

        runResult.steps = stepResults.ToArray();
        Directory.CreateDirectory(runResult.resultDirectory);
        string finalPath = Path.Combine(runResult.resultDirectory, "result.json");
        string temporaryPath = finalPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonUtility.ToJson(runResult, true));
        if (File.Exists(finalPath))
            File.Delete(finalPath);
        File.Move(temporaryPath, finalPath);
    }
}
#endif
