using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonPortalTransportPoC.Editor
{
    /// <summary>
    /// Runs only the isolated Portal Transport PoC EditMode assembly.
    /// This is a structural/code gate, never visual acceptance evidence.
    /// </summary>
    public static class DungeonPortalTargetedEditModeTestRunner
    {
        private const string ExpectedScenePath =
            "Assets/Experiments/DungeonPortalTransportPoC/Scenes/Start_Admin_PortalTransportValidation.unity";
        private const string TestAssemblyName = "DungeonPortalTransportPoC.Tests";

        private static bool running;

        [MenuItem("Tools/Dungeon/Lighting/Portal Transport PoC/Run Targeted EditMode Tests (Clean Scene Only)")]
        public static void RunFromMenu()
        {
            string result = Run();
            if (result.StartsWith("STRUCTURAL_TEST_GATE_ONLY|status=PASS", StringComparison.Ordinal))
                Debug.Log(result);
            else
                Debug.LogError(result);
        }

        public static string Run()
        {
            if (running)
                throw new InvalidOperationException("A targeted Portal Transport PoC test run is already active.");

            Scene sceneBefore = SceneManager.GetActiveScene();
            AssertSafePreflight(sceneBefore);
            string scenePathBefore = sceneBefore.path;
            string selectionGlobalId = Selection.activeObject == null
                ? string.Empty
                : GlobalObjectId.GetGlobalObjectIdSlow(Selection.activeObject).ToString();

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var callbacks = new ResultCallbacks();
            bool callbacksRegistered = false;
            running = true;

            try
            {
                api.RegisterCallbacks(callbacks, 1000);
                callbacksRegistered = true;
                string runId = api.Execute(new ExecutionSettings(new Filter
                {
                    testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                    assemblyNames = new[] { TestAssemblyName }
                })
                {
                    runSynchronously = true
                });

                if (!callbacks.Finished || callbacks.Result == null)
                {
                    throw new InvalidOperationException(
                        "The synchronous targeted test run returned without a final result. runId=" + runId);
                }

                Scene sceneAfter = SceneManager.GetActiveScene();
                if (sceneAfter.path != scenePathBefore)
                {
                    throw new InvalidOperationException(
                        "Targeted tests changed the active scene. before=" + scenePathBefore +
                        ", after=" + sceneAfter.path);
                }

                if (sceneAfter.isDirty)
                {
                    throw new InvalidOperationException(
                        "Targeted tests dirtied the validation scene. No save was performed.");
                }

                ITestResultAdaptor result = callbacks.Result;
                int total = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
                string failures = callbacks.Failures.Count == 0
                    ? "<none>"
                    : string.Join(" || ", callbacks.Failures);
                bool passed = total > 0 &&
                              result.FailCount == 0 &&
                              result.SkipCount == 0 &&
                              result.InconclusiveCount == 0;

                return "STRUCTURAL_TEST_GATE_ONLY" +
                       "|status=" + (passed ? "PASS" : "FAIL") +
                       "|runId=" + runId +
                       "|assembly=" + TestAssemblyName +
                       "|total=" + total +
                       "|passed=" + result.PassCount +
                       "|failed=" + result.FailCount +
                       "|skipped=" + result.SkipCount +
                       "|inconclusive=" + result.InconclusiveCount +
                       "|sceneDirty=" + sceneAfter.isDirty +
                       "|failures=" + failures;
            }
            finally
            {
                try
                {
                    if (callbacksRegistered)
                        api.UnregisterCallbacks(callbacks);
                    UnityEngine.Object.DestroyImmediate(api);
                }
                finally
                {
                    running = false;
                    RestoreSelection(selectionGlobalId);
                }
            }
        }

        private static void AssertSafePreflight(Scene scene)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Targeted EditMode tests require stable Edit Mode.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Targeted EditMode tests cannot run during compile/import.");
            if (Lightmapping.isRunning)
                throw new InvalidOperationException("Targeted EditMode tests cannot run during lightmapping.");
            if (!scene.IsValid() || !scene.isLoaded || scene.path != ExpectedScenePath)
            {
                throw new InvalidOperationException(
                    "Open the isolated validation scene before running targeted tests. active=" + scene.path);
            }

            if (scene.isDirty)
                throw new InvalidOperationException("Refusing to run tests with a dirty validation scene.");
        }

        private static void RestoreSelection(string selectionGlobalId)
        {
            if (string.IsNullOrEmpty(selectionGlobalId))
            {
                Selection.activeObject = null;
                return;
            }

            if (!GlobalObjectId.TryParse(selectionGlobalId, out GlobalObjectId globalId))
                throw new InvalidOperationException("Unable to parse the pre-test selection identity.");

            UnityEngine.Object restored = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId);
            if (restored == null)
                throw new InvalidOperationException("Unable to restore the pre-test selection after scene reload.");
            Selection.activeObject = restored;
        }

        private sealed class ResultCallbacks : ICallbacks
        {
            public bool Finished { get; private set; }
            public ITestResultAdaptor Result { get; private set; }
            public List<string> Failures { get; } = new List<string>();

            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                Result = result;
                Finished = true;
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren && result.FailCount > 0)
                {
                    string message = string.IsNullOrWhiteSpace(result.Message)
                        ? "<no message>"
                        : string.Join(" ", result.Message.Split(new[] { '\r', '\n' },
                            StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()));
                    Failures.Add(result.FullName + ": " + message);
                }
            }
        }
    }
}
