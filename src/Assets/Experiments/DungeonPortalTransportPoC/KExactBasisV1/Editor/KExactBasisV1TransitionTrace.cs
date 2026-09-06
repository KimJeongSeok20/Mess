using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DungeonPortalTransportPoC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonPortalTransportPoC.KExactBasisV1.Editor
{
    /// <summary>
    /// Records the real, frame-driven smoothing path of the isolated K-exact runtime.
    /// The trace starts only in an already-running canonical Play Mode scene. It never
    /// enters/exits Play Mode and never calls TryApplyCurrentInputsImmediately while a
    /// measured segment is active. The one immediate call is reserved for the final
    /// rollback, after event sampling has been detached, so the user's live state is
    /// restored before the asynchronous job reports completion.
    /// </summary>
    public static class KExactBasisV1TransitionTrace
    {
        public const string Schema = "KExactBasisV1DynamicTransitionTrace/v1";
        public const string Status = "KEXACT_BASIS_V1_DYNAMIC_TRANSITION_TRACE";

        private const string ExpectedConnectionKey =
            "Start_Admin_R000_Door_SM_A_KExactBasisV1";
        private const string ResultsRoot =
            "Assets/Experiments/DungeonPortalTransportPoC/KExactBasisV1/" +
            "Diagnostics/DynamicTransitionTrace/Results";

        private static TraceSession activeSession;
        private static string lastResult = "NOT_RUN " + Status;

        public static bool IsRunning => activeSession != null;
        public static string LastResult => lastResult;

        [MenuItem(
            "Tools/Dungeon/Lighting/Portal Transport PoC/KExactBasisV1/" +
            "Trace Dynamic Transitions (Play Mode; No Mode Toggle)")]
        public static void StartFromMenu()
        {
            string result = StartTrace();
            if (result.StartsWith("STARTED", StringComparison.Ordinal))
                Debug.Log(result);
            else
                Debug.LogError(result);
        }

        [MenuItem(
            "Tools/Dungeon/Lighting/Portal Transport PoC/KExactBasisV1/" +
            "Trace Dynamic Transitions (Play Mode; No Mode Toggle)", true)]
        private static bool ValidateStartFromMenu()
        {
            return activeSession == null && Application.isPlaying &&
                   EditorApplication.isPlaying && !EditorApplication.isPaused;
        }

        /// <summary>REST/static-method entry point. Completion is logged asynchronously.</summary>
        public static void StartCli()
        {
            string result = StartTrace();
            if (!result.StartsWith("STARTED", StringComparison.Ordinal))
                throw new InvalidOperationException(result);
            Debug.Log(result);
        }

        /// <summary>REST/static-method status endpoint.</summary>
        public static string GetStatusCli()
        {
            string result = activeSession != null
                ? activeSession.ProgressText
                : lastResult;
            Debug.Log(result);
            return result;
        }

        public static string StartTrace()
        {
            if (activeSession != null)
                return "FAIL " + Status + ": a transition trace is already running.";

            try
            {
                var session = new TraceSession();
                activeSession = session;
                session.Begin();
                lastResult = "STARTED " + Status + "\n" +
                             "sessionId=" + session.SessionId + "\n" +
                             "playModeToggled=false\n" +
                             "completion=asynchronous";
                return lastResult;
            }
            catch (Exception exception)
            {
                activeSession = null;
                lastResult = "FAIL " + Status + ": " + exception;
                return lastResult;
            }
        }

        private static void SessionCompleted(TraceSession session, string result)
        {
            if (ReferenceEquals(activeSession, session))
                activeSession = null;
            lastResult = result;
        }

        private enum Phase
        {
            InitialSettle,
            StartRise,
            StartFall,
            AdministrativeRise,
            AdministrativeFall,
            DoorPreparation,
            DoorOpening,
            DoorOpenSettle,
            DoorClosing,
            DoorClosedSettle,
            Completing
        }

        private enum SegmentKind
        {
            StartRise,
            StartFall,
            AdministrativeRise,
            AdministrativeFall,
            DoorOpen,
            DoorClose
        }

        private sealed class TraceSession
        {
            private const float ConvergenceTolerance = 0.001f;
            private const float MonotonicTolerance = 0.00025f;
            private const float ConstantTolerance = 0.0005f;
            private const double SetupTimeoutSeconds = 10d;
            private const double SegmentTimeoutSeconds = 8d;
            private const double DoorMotionSeconds = 0.65d;
            private const int RequiredConsecutiveStableFrames = 2;
            private const int RequiredDistinctWeightFrames = 3;

            private readonly DateTime startedUtc = DateTime.UtcNow;
            private readonly string sessionId;
            private readonly Scene scene;
            private readonly bool initialSceneDirty;
            private readonly string initialBuiltSceneSha;
            private readonly string initialBuildManifestSha;
            private readonly KExactBasisV1EditorContract.SelectionSnapshot selection;
            private readonly KExactBasisV1EditorContract.ProtectedAssetSnapshot
                protectedAssets;
            private readonly KExactBasisV1EditorContract.SceneBindings bindings;
            private readonly RuntimeHandles runtime;
            private readonly SmoothingSnapshot originalSmoothing;
            private readonly DoorConfiguration door;
            private readonly DungeonTileLightmapSwitcher.PowerLevel originalStartPower;
            private readonly DungeonTileLightmapSwitcher.PowerLevel
                originalAdministrativePower;
            private readonly Quaternion originalDoorRotation;
            private readonly float originalStartRaw;
            private readonly float originalAdministrativeRaw;
            private readonly float originalDoorRaw;
            private readonly List<TraceRecord> records = new List<TraceRecord>(2048);
            private readonly List<SegmentResult> segmentResults =
                new List<SegmentResult>(6);
            private readonly List<string> relevantRuntimeErrors = new List<string>();

            private Phase phase;
            private SegmentResult currentSegment;
            private double phaseStartedEditorTime;
            private double phaseDeadlineEditorTime;
            private double doorMotionStartedEditorTime;
            private int consecutiveStableFrames;
            private int lastTickFrame = -1;
            private int recordSequence;
            private string callbackFailure;
            private bool callbacksAttached;
            private bool completing;

            internal string SessionId => sessionId;

            internal string ProgressText =>
                "RUNNING " + Status + "\n" +
                "sessionId=" + sessionId + "\n" +
                "phase=" + phase + "\n" +
                "recordCount=" + records.Count.ToString(CultureInfo.InvariantCulture) +
                "\ncompletedSegmentCount=" +
                segmentResults.Count(value => value.Passed)
                    .ToString(CultureInfo.InvariantCulture);

            internal TraceSession()
            {
                scene = RequireRuntimeTraceScene();
                initialSceneDirty = scene.isDirty;
                initialBuiltSceneSha = KExactBasisV1EditorContract.ComputeFileSha256(
                    KExactBasisV1EditorContract.BuiltScenePath);
                initialBuildManifestSha = KExactBasisV1EditorContract.ComputeFileSha256(
                    KExactBasisV1EditorContract.BuildManifestPath);
                selection = KExactBasisV1EditorContract.SelectionSnapshot.Capture();
                protectedAssets =
                    KExactBasisV1EditorContract.ProtectedAssetSnapshot.Capture();
                bindings = KExactBasisV1EditorContract.ResolveSceneBindings(
                    scene, false, true, false);
                runtime = ResolveRuntimeHandles(bindings);
                originalSmoothing = SmoothingSnapshot.Capture(runtime.Connection);
                door = DoorConfiguration.Capture(runtime.DoorAngleSource);
                originalStartPower = bindings.StartSwitcher.CurrentPowerLevel;
                originalAdministrativePower =
                    bindings.AdministrativeSwitcher.CurrentPowerLevel;
                originalDoorRotation = bindings.DoorLeaf.localRotation;
                originalStartRaw = ReadScalar(runtime.StartPower, "Start power");
                originalAdministrativeRaw = ReadScalar(
                    runtime.AdministrativePower, "Administrative power");
                originalDoorRaw = ReadScalar(runtime.DoorSource, "Door aperture");
                sessionId = "T" + startedUtc.ToString(
                    "yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + "_" +
                    Guid.NewGuid().ToString("N").Substring(0, 8);

                if (!Approximately(runtime.Connection.PowerAToB01, originalStartRaw) ||
                    !Approximately(
                        runtime.Connection.PowerBToA01,
                        originalAdministrativeRaw) ||
                    !Approximately(
                        runtime.Connection.SmoothedDoorOpenness01,
                        originalDoorRaw))
                {
                    throw new InvalidOperationException(
                        "Runtime is already mid-transition. Wait for a stable state before " +
                        "starting the trace.");
                }
            }

            internal void Begin()
            {
                AttachCallbacks();
                try
                {
                    runtime.Connection.ConfigureSmoothing(0.35f, 0.5f, 0.2f, 0.2f);
                    SetPower(false, false);
                    SetDoorFraction(1f);
                    EnterPhase(Phase.InitialSettle, SetupTimeoutSeconds);
                }
                catch (Exception beginFailure)
                {
                    DetachCallbacks();
                    RestoreReport restore = RestoreState();
                    if (!restore.Passed)
                    {
                        throw new AggregateException(
                            "Transition trace setup failed and rollback was incomplete: " +
                            restore.Failure,
                            beginFailure);
                    }
                    throw;
                }
            }

            private void AttachCallbacks()
            {
                runtime.Connection.WeightsChanged += OnWeightsChanged;
                Application.logMessageReceived += OnLogMessage;
                EditorApplication.update += Tick;
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
                EditorApplication.quitting += OnEditorQuitting;
                callbacksAttached = true;
            }

            private void DetachCallbacks()
            {
                if (!callbacksAttached)
                    return;
                callbacksAttached = false;
                runtime.Connection.WeightsChanged -= OnWeightsChanged;
                Application.logMessageReceived -= OnLogMessage;
                EditorApplication.update -= Tick;
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
                EditorApplication.quitting -= OnEditorQuitting;
            }

            private void OnBeforeAssemblyReload()
            {
                Abort("Assembly reload began during the transition trace.");
            }

            private void OnEditorQuitting()
            {
                Abort("Unity Editor quit began during the transition trace.");
            }

            private void OnPlayModeStateChanged(PlayModeStateChange state)
            {
                if (state == PlayModeStateChange.ExitingPlayMode ||
                    state == PlayModeStateChange.EnteredEditMode)
                {
                    Abort("Play Mode changed during the transition trace; this tool did " +
                          "not request the mode change.");
                }
            }

            private void OnLogMessage(string condition, string stackTrace, LogType type)
            {
                if (type != LogType.Error && type != LogType.Exception &&
                    type != LogType.Assert)
                    return;
                if ((condition ?? string.Empty).IndexOf(
                        "KExactPortal", StringComparison.OrdinalIgnoreCase) < 0 &&
                    (condition ?? string.Empty).IndexOf(
                        ExpectedConnectionKey, StringComparison.Ordinal) < 0)
                    return;
                relevantRuntimeErrors.Add(
                    KExactBasisV1EditorContract.Sanitize(condition) + " | " +
                    KExactBasisV1EditorContract.Sanitize(stackTrace));
            }

            private void OnWeightsChanged(KExactTransportWeights weights)
            {
                if (completing || currentSegment == null)
                    return;
                try
                {
                    Record("WeightsChanged", weights);
                }
                catch (Exception exception)
                {
                    // Runtime observers must never throw into KExactPortalConnection.
                    callbackFailure = exception.ToString();
                }
            }

            private void Tick()
            {
                if (completing)
                    return;
                try
                {
                    RequireLiveRuntime();
                    if (!string.IsNullOrEmpty(callbackFailure))
                        throw new InvalidOperationException(
                            "WeightsChanged observer failed: " + callbackFailure);
                    if (relevantRuntimeErrors.Count != 0)
                        throw new InvalidOperationException(
                            "K-exact runtime logged an error: " +
                            relevantRuntimeErrors[relevantRuntimeErrors.Count - 1]);

                    int frame = Time.frameCount;
                    if (frame == lastTickFrame)
                        return;
                    lastTickFrame = frame;
                    if (currentSegment != null)
                        Record("FrameSample", runtime.Connection.CurrentWeights);

                    switch (phase)
                    {
                        case Phase.InitialSettle:
                            if (WaitForStable(0f, 0f, 1f, "initial P00/D100"))
                                BeginStartRise();
                            break;
                        case Phase.StartRise:
                            if (WaitForStable(1f, 0f, 1f, "Start rise"))
                            {
                                EndMeasuredSegment();
                                BeginStartFall();
                            }
                            break;
                        case Phase.StartFall:
                            if (WaitForStable(0f, 0f, 1f, "Start fall"))
                            {
                                EndMeasuredSegment();
                                BeginAdministrativeRise();
                            }
                            break;
                        case Phase.AdministrativeRise:
                            if (WaitForStable(0f, 1f, 1f, "Administrative rise"))
                            {
                                EndMeasuredSegment();
                                BeginAdministrativeFall();
                            }
                            break;
                        case Phase.AdministrativeFall:
                            if (WaitForStable(0f, 0f, 1f, "Administrative fall"))
                            {
                                EndMeasuredSegment();
                                BeginDoorPreparation();
                            }
                            break;
                        case Phase.DoorPreparation:
                            if (WaitForStable(1f, 1f, 0f, "door preparation P11/D000"))
                                BeginDoorOpen();
                            break;
                        case Phase.DoorOpening:
                            TickDoorMotion(true);
                            break;
                        case Phase.DoorOpenSettle:
                            if (WaitForStable(1f, 1f, 1f, "Door open"))
                            {
                                EndMeasuredSegment();
                                BeginDoorClose();
                            }
                            break;
                        case Phase.DoorClosing:
                            TickDoorMotion(false);
                            break;
                        case Phase.DoorClosedSettle:
                            if (WaitForStable(1f, 1f, 0f, "Door close"))
                            {
                                EndMeasuredSegment();
                                Finish(true, null);
                            }
                            break;
                    }
                }
                catch (Exception exception)
                {
                    Finish(false, exception.ToString());
                }
            }

            private void BeginStartRise()
            {
                BeginMeasuredSegment(SegmentKind.StartRise, 0f, 1f);
                SetPower(true, false);
                EnterPhase(Phase.StartRise, SegmentTimeoutSeconds);
            }

            private void BeginStartFall()
            {
                BeginMeasuredSegment(SegmentKind.StartFall, 1f, 0f);
                SetPower(false, false);
                EnterPhase(Phase.StartFall, SegmentTimeoutSeconds);
            }

            private void BeginAdministrativeRise()
            {
                BeginMeasuredSegment(SegmentKind.AdministrativeRise, 0f, 1f);
                SetPower(false, true);
                EnterPhase(Phase.AdministrativeRise, SegmentTimeoutSeconds);
            }

            private void BeginAdministrativeFall()
            {
                BeginMeasuredSegment(SegmentKind.AdministrativeFall, 1f, 0f);
                SetPower(false, false);
                EnterPhase(Phase.AdministrativeFall, SegmentTimeoutSeconds);
            }

            private void BeginDoorPreparation()
            {
                currentSegment = null;
                SetPower(true, true);
                SetDoorFraction(0f);
                EnterPhase(Phase.DoorPreparation, SetupTimeoutSeconds);
            }

            private void BeginDoorOpen()
            {
                BeginMeasuredSegment(SegmentKind.DoorOpen, 0f, 1f);
                doorMotionStartedEditorTime = EditorApplication.timeSinceStartup;
                EnterPhase(Phase.DoorOpening, SegmentTimeoutSeconds);
            }

            private void BeginDoorClose()
            {
                BeginMeasuredSegment(SegmentKind.DoorClose, 1f, 0f);
                doorMotionStartedEditorTime = EditorApplication.timeSinceStartup;
                EnterPhase(Phase.DoorClosing, SegmentTimeoutSeconds);
            }

            private void TickDoorMotion(bool opening)
            {
                EnsureBeforeDeadline(opening ? "Door opening" : "Door closing");
                double elapsed = EditorApplication.timeSinceStartup -
                                 doorMotionStartedEditorTime;
                float normalized = Mathf.Clamp01((float)(elapsed / DoorMotionSeconds));
                float eased = Mathf.SmoothStep(0f, 1f, normalized);
                SetDoorFraction(opening ? eased : 1f - eased);
                if (normalized < 1f)
                    return;

                SetDoorFraction(opening ? 1f : 0f);
                EnterPhase(
                    opening ? Phase.DoorOpenSettle : Phase.DoorClosedSettle,
                    SegmentTimeoutSeconds);
            }

            private void BeginMeasuredSegment(
                SegmentKind kind,
                float expectedStart,
                float expectedEnd)
            {
                if (currentSegment != null)
                    throw new InvalidOperationException(
                        "A measured segment was started before its predecessor ended.");
                currentSegment = new SegmentResult(
                    kind,
                    expectedStart,
                    expectedEnd,
                    Time.frameCount,
                    Time.unscaledTimeAsDouble);
                segmentResults.Add(currentSegment);
                Record("SegmentBegin", runtime.Connection.CurrentWeights);
            }

            private void EndMeasuredSegment()
            {
                if (currentSegment == null)
                    throw new InvalidOperationException("No measured segment is active.");
                Record("SegmentConverged", runtime.Connection.CurrentWeights);
                ValidateSegment(currentSegment);
                currentSegment = null;
            }

            private void EnterPhase(
                Phase next,
                double timeoutSeconds,
                bool resetStableFrames = true)
            {
                phase = next;
                phaseStartedEditorTime = EditorApplication.timeSinceStartup;
                phaseDeadlineEditorTime = phaseStartedEditorTime + timeoutSeconds;
                if (resetStableFrames)
                    consecutiveStableFrames = 0;
            }

            private bool WaitForStable(
                float expectedStart,
                float expectedAdministrative,
                float expectedDoorOpen,
                string label)
            {
                EnsureBeforeDeadline(label);
                if (IsStable(
                        expectedStart,
                        expectedAdministrative,
                        expectedDoorOpen))
                {
                    consecutiveStableFrames++;
                }
                else
                {
                    consecutiveStableFrames = 0;
                }
                return consecutiveStableFrames >= RequiredConsecutiveStableFrames;
            }

            private bool IsStable(
                float expectedStart,
                float expectedAdministrative,
                float expectedDoorOpen)
            {
                float rawStart = ReadScalar(runtime.StartPower, "Start power");
                float rawAdministrative = ReadScalar(
                    runtime.AdministrativePower, "Administrative power");
                float rawDoor = ReadScalar(runtime.DoorSource, "Door aperture");
                float expectedAperture =
                    PortalTransportMath.ComputeProjectedApertureFraction(expectedDoorOpen);
                KExactTransportWeights weights = runtime.Connection.CurrentWeights;
                return Approximately(rawStart, expectedStart) &&
                       Approximately(rawAdministrative, expectedAdministrative) &&
                       Approximately(runtime.DoorAngleSource.OpenFraction, expectedDoorOpen) &&
                       Approximately(rawDoor, expectedAperture) &&
                       ApproximatelySettled(weights.PowerAToB01, expectedStart) &&
                       ApproximatelySettled(
                           weights.PowerBToA01, expectedAdministrative) &&
                       ApproximatelySettled(
                           weights.DoorOpenness01, expectedAperture);
            }

            private void EnsureBeforeDeadline(string label)
            {
                if (EditorApplication.timeSinceStartup <= phaseDeadlineEditorTime)
                    return;
                throw new TimeoutException(
                    label + " did not converge before its " +
                    (phaseDeadlineEditorTime - phaseStartedEditorTime).ToString(
                        "R", CultureInfo.InvariantCulture) + " second deadline.");
            }

            private void SetPower(bool startOn, bool administrativeOn)
            {
                bindings.StartSwitcher.SetPowerLevel(
                    startOn
                        ? DungeonTileLightmapSwitcher.PowerLevel.P100
                        : DungeonTileLightmapSwitcher.PowerLevel.P0);
                bindings.AdministrativeSwitcher.SetPowerLevel(
                    administrativeOn
                        ? DungeonTileLightmapSwitcher.PowerLevel.P100
                        : DungeonTileLightmapSwitcher.PowerLevel.P0);
            }

            private void SetDoorFraction(float openFraction)
            {
                bindings.DoorLeaf.localRotation = door.RotationFor(openFraction);
                Physics.SyncTransforms();
                runtime.DoorAngleSource.EvaluateNow();
            }

            private void Record(string callbackKind, KExactTransportWeights weights)
            {
                float rawStart = ReadScalar(runtime.StartPower, "Start power");
                float rawAdministrative = ReadScalar(
                    runtime.AdministrativePower, "Administrative power");
                float rawDoorAperture = ReadScalar(runtime.DoorSource, "Door aperture");
                records.Add(new TraceRecord(
                    recordSequence++,
                    currentSegment != null ? currentSegment.Kind.ToString() : "SETUP",
                    callbackKind,
                    EditorApplication.timeSinceStartup,
                    Time.unscaledTimeAsDouble,
                    Time.frameCount,
                    currentSegment != null ? currentSegment.ExpectedStart : float.NaN,
                    currentSegment != null ? currentSegment.ExpectedEnd : float.NaN,
                    rawStart,
                    rawAdministrative,
                    runtime.DoorAngleSource.OpenFraction,
                    rawDoorAperture,
                    runtime.DoorAngleSource.OpenFraction * door.OpenAngleDegrees,
                    weights.PowerAToB01,
                    weights.PowerBToA01,
                    weights.DoorOpenness01,
                    weights.DirectScaleAToB,
                    weights.DirectScaleBToA,
                    runtime.Connection.ReceiverBounceTotalIntensityAToB,
                    runtime.Connection.ReceiverBounceTotalIntensityBToA,
                    weights.ReflectionWeightAToB,
                    weights.ReflectionWeightBToA,
                    runtime.Connection.DoorSurfaceTotalIntensityAToB,
                    runtime.Connection.DoorSurfaceTotalIntensityBToA,
                    runtime.Manager.IsActive,
                    runtime.Connection.IsTransportActive,
                    runtime.Manager.IsFaultLatched ||
                    runtime.Connection.IsFaultLatched,
                    JoinFaultReasons()));
            }

            private string JoinFaultReasons()
            {
                return string.Join(
                    " | ",
                    new[] { runtime.Manager.FaultReason, runtime.Connection.FaultReason }
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
            }

            private void ValidateSegment(SegmentResult result)
            {
                List<TraceRecord> rows = records
                    .Where(value => value.SegmentId == result.Kind.ToString())
                    .OrderBy(value => value.Sequence)
                    .ToList();
                List<TraceRecord> weightRows = rows
                    .Where(value => value.CallbackKind == "WeightsChanged")
                    .ToList();
                if (rows.Count < 4)
                    throw new InvalidOperationException(
                        result.Kind + " has too few trace records: " + rows.Count + ".");

                Func<TraceRecord, float> primary = result.IsStart
                    ? value => value.SmoothedStart
                    : result.IsAdministrative
                        ? value => value.SmoothedAdministrative
                        : value => value.SmoothedDoorAperture;
                float first = primary(rows[0]);
                float last = primary(rows[rows.Count - 1]);
                AssertApproximately(first, result.ExpectedStart, result.Kind + " start");
                AssertApproximately(last, result.ExpectedEnd, result.Kind + " end");

                bool rising = result.ExpectedEnd > result.ExpectedStart;
                ValidateMonotonic(rows, primary, rising, result.Kind + " primary");
                ValidateNoOvershoot(
                    rows,
                    primary,
                    result.ExpectedStart,
                    result.ExpectedEnd,
                    result.Kind + " primary");

                if (result.IsStart)
                {
                    ValidateTransportSeries(rows, rising, result.Kind, true);
                    ValidatePowerIsolation(rows, result.Kind, true);
                }
                else if (result.IsAdministrative)
                {
                    ValidateTransportSeries(rows, rising, result.Kind, false);
                    ValidatePowerIsolation(rows, result.Kind, false);
                }
                else
                {
                    ValidateMonotonic(
                        rows, value => value.RawDoorOpen, rising,
                        result.Kind + " raw door angle");
                    ValidateMonotonic(
                        rows, value => value.RawDoorAperture, rising,
                        result.Kind + " raw aperture");
                    ValidateMonotonic(
                        rows, value => value.DirectAToB, rising,
                        result.Kind + " direct AToB");
                    ValidateMonotonic(
                        rows, value => value.DirectBToA, rising,
                        result.Kind + " direct BToA");
                    ValidateMonotonic(
                        rows, value => value.BounceAToB, rising,
                        result.Kind + " bounce AToB");
                    ValidateMonotonic(
                        rows, value => value.BounceBToA, rising,
                        result.Kind + " bounce BToA");
                    ValidateMonotonic(
                        rows, value => value.ReflectionAToB, rising,
                        result.Kind + " reflection AToB");
                    ValidateMonotonic(
                        rows, value => value.ReflectionBToA, rising,
                        result.Kind + " reflection BToA");
                    ValidateConstant(
                        rows, value => value.DoorSurfaceAToB,
                        result.Kind + " door surface AToB");
                    ValidateConstant(
                        rows, value => value.DoorSurfaceBToA,
                        result.Kind + " door surface BToA");
                }

                int distinctWeightFrames = weightRows
                    .Select(value => value.Frame)
                    .Distinct()
                    .Count();
                if (distinctWeightFrames < RequiredDistinctWeightFrames)
                {
                    throw new InvalidOperationException(
                        result.Kind + " emitted WeightsChanged on only " +
                        distinctWeightFrames + " distinct frames; at least " +
                        RequiredDistinctWeightFrames + " are required.");
                }
                if (rows.Any(value => value.FaultLatched || !value.ManagerActive ||
                                      !value.ConnectionActive ||
                                      !string.IsNullOrEmpty(value.FaultReason)))
                    throw new InvalidOperationException(
                        result.Kind + " observed an inactive or faulted runtime row.");
                if (rows.Any(value => !value.AllTelemetryFiniteAndNonNegative))
                    throw new InvalidOperationException(
                        result.Kind + " contains non-finite or negative telemetry.");
                if (!runtime.Connection.TryValidateParity(out string parityFailure))
                    throw new InvalidOperationException(
                        result.Kind + " parity failed at convergence: " + parityFailure);

                result.Complete(
                    rows.Count,
                    weightRows.Count,
                    distinctWeightFrames,
                    rows[rows.Count - 1].UnscaledTime - rows[0].UnscaledTime);
            }

            private static void ValidateTransportSeries(
                List<TraceRecord> rows,
                bool rising,
                SegmentKind kind,
                bool aToB)
            {
                var series = aToB
                    ? new (string, Func<TraceRecord, float>)[]
                    {
                        ("direct AToB", value => value.DirectAToB),
                        ("bounce AToB", value => value.BounceAToB),
                        ("reflection AToB", value => value.ReflectionAToB),
                        ("door surface AToB", value => value.DoorSurfaceAToB)
                    }
                    : new (string, Func<TraceRecord, float>)[]
                    {
                        ("direct BToA", value => value.DirectBToA),
                        ("bounce BToA", value => value.BounceBToA),
                        ("reflection BToA", value => value.ReflectionBToA),
                        ("door surface BToA", value => value.DoorSurfaceBToA)
                    };
                for (int i = 0; i < series.Length; i++)
                {
                    ValidateMonotonic(
                        rows, series[i].Item2, rising,
                        kind + " " + series[i].Item1);
                    float start = series[i].Item2(rows[0]);
                    float end = series[i].Item2(rows[rows.Count - 1]);
                    ValidateNoOvershoot(
                        rows, series[i].Item2, start, end,
                        kind + " " + series[i].Item1);
                }
            }

            private static void ValidatePowerIsolation(
                List<TraceRecord> rows,
                SegmentKind kind,
                bool aToBIsMoving)
            {
                ValidateConstant(
                    rows, value => value.RawDoorOpen,
                    kind + " raw door angle");
                ValidateConstant(
                    rows, value => value.RawDoorAperture,
                    kind + " raw door aperture");
                ValidateConstant(
                    rows, value => value.SmoothedDoorAperture,
                    kind + " smoothed door aperture");

                if (aToBIsMoving)
                {
                    ValidateConstant(
                        rows, value => value.RawAdministrative,
                        kind + " raw opposite Admin power");
                    ValidateConstant(
                        rows, value => value.SmoothedAdministrative,
                        kind + " smoothed opposite Admin power");
                    ValidateConstant(
                        rows, value => value.DirectBToA,
                        kind + " opposite direct BToA");
                    ValidateConstant(
                        rows, value => value.BounceBToA,
                        kind + " opposite bounce BToA");
                    ValidateConstant(
                        rows, value => value.ReflectionBToA,
                        kind + " opposite residual reflection BToA");
                    ValidateConstant(
                        rows, value => value.DoorSurfaceBToA,
                        kind + " opposite door surface BToA");
                    AssertApproximately(
                        rows[rows.Count - 1].SmoothedAdministrative,
                        0f,
                        kind + " opposite Admin power");
                    AssertNearZero(
                        rows, value => value.DirectBToA,
                        kind + " opposite direct BToA");
                    AssertNearZero(
                        rows, value => value.BounceBToA,
                        kind + " opposite bounce BToA");
                    AssertNearZero(
                        rows, value => value.DoorSurfaceBToA,
                        kind + " opposite door surface BToA");
                }
                else
                {
                    ValidateConstant(
                        rows, value => value.RawStart,
                        kind + " raw opposite Start power");
                    ValidateConstant(
                        rows, value => value.SmoothedStart,
                        kind + " smoothed opposite Start power");
                    ValidateConstant(
                        rows, value => value.DirectAToB,
                        kind + " opposite direct AToB");
                    ValidateConstant(
                        rows, value => value.BounceAToB,
                        kind + " opposite bounce AToB");
                    ValidateConstant(
                        rows, value => value.ReflectionAToB,
                        kind + " opposite residual reflection AToB");
                    ValidateConstant(
                        rows, value => value.DoorSurfaceAToB,
                        kind + " opposite door surface AToB");
                    AssertApproximately(
                        rows[rows.Count - 1].SmoothedStart,
                        0f,
                        kind + " opposite Start power");
                    AssertNearZero(
                        rows, value => value.DirectAToB,
                        kind + " opposite direct AToB");
                    AssertNearZero(
                        rows, value => value.BounceAToB,
                        kind + " opposite bounce AToB");
                    AssertNearZero(
                        rows, value => value.DoorSurfaceAToB,
                        kind + " opposite door surface AToB");
                }
            }

            private static void AssertNearZero(
                List<TraceRecord> rows,
                Func<TraceRecord, float> selector,
                string label)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (Mathf.Abs(selector(rows[i])) > ConstantTolerance)
                        throw new InvalidOperationException(
                            label + " leaked at record " + i + ".");
                }
            }

            private static void ValidateMonotonic(
                List<TraceRecord> rows,
                Func<TraceRecord, float> selector,
                bool rising,
                string label)
            {
                float previous = selector(rows[0]);
                for (int i = 1; i < rows.Count; i++)
                {
                    float current = selector(rows[i]);
                    if ((rising && current + MonotonicTolerance < previous) ||
                        (!rising && current - MonotonicTolerance > previous))
                    {
                        throw new InvalidOperationException(
                            label + " is not monotonic at record " + i +
                            ": previous=" + Format(previous) +
                            ", current=" + Format(current) + ".");
                    }
                    previous = current;
                }
            }

            private static void ValidateNoOvershoot(
                List<TraceRecord> rows,
                Func<TraceRecord, float> selector,
                float start,
                float end,
                string label)
            {
                float minimum = Mathf.Min(start, end) - MonotonicTolerance;
                float maximum = Mathf.Max(start, end) + MonotonicTolerance;
                for (int i = 0; i < rows.Count; i++)
                {
                    float value = selector(rows[i]);
                    if (value < minimum || value > maximum)
                    {
                        throw new InvalidOperationException(
                            label + " overshot its endpoints at record " + i +
                            ": value=" + Format(value) + ", range=[" +
                            Format(minimum) + "," + Format(maximum) + "].");
                    }
                }
            }

            private static void ValidateConstant(
                List<TraceRecord> rows,
                Func<TraceRecord, float> selector,
                string label)
            {
                float expected = selector(rows[0]);
                for (int i = 1; i < rows.Count; i++)
                {
                    if (Mathf.Abs(selector(rows[i]) - expected) > ConstantTolerance)
                        throw new InvalidOperationException(
                            label + " changed while only door aperture moved.");
                }
            }

            private static void AssertApproximately(float actual, float expected, string label)
            {
                if (!Approximately(actual, expected))
                    throw new InvalidOperationException(
                        label + " did not converge within " +
                        Format(ConvergenceTolerance) + ": actual=" + Format(actual) +
                        ", expected=" + Format(expected) + ".");
            }

            private void RequireLiveRuntime()
            {
                if (!Application.isPlaying || !EditorApplication.isPlaying ||
                    EditorApplication.isPlayingOrWillChangePlaymode !=
                    EditorApplication.isPlaying || EditorApplication.isPaused)
                    throw new InvalidOperationException(
                        "Stable, unpaused Play Mode was lost. The tracer never toggles it.");
                if (EditorApplication.isCompiling || Lightmapping.isRunning)
                    throw new InvalidOperationException(
                        "Compilation or lightmapping began during the trace.");
                Scene active = SceneManager.GetActiveScene();
                if (!active.IsValid() || !active.isLoaded ||
                    active.handle != scene.handle ||
                    KExactBasisV1EditorContract.CountLoadedNonPreviewScenes() != 1 ||
                    SceneManager.sceneCount != 1 ||
                    !string.Equals(
                        KExactBasisV1EditorContract.NormalizePath(active.path),
                        KExactBasisV1EditorContract.BuiltScenePath,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "The canonical single-scene Play Mode contract changed.");
                runtime.RequireStableActiveState();
            }

            private void Abort(string reason)
            {
                if (!completing)
                    Finish(false, reason);
            }

            private void Finish(bool requestedPass, string failure)
            {
                if (completing)
                    return;
                completing = true;
                phase = Phase.Completing;
                DetachCallbacks();
                currentSegment = null;

                RestoreReport restore;
                try
                {
                    restore = RestoreState();
                }
                catch (Exception exception)
                {
                    restore = new RestoreReport(false, exception.ToString(), false);
                }

                bool allSegmentsPassed = segmentResults.Count == 6 &&
                                         segmentResults.All(value => value.Passed);
                bool passed = requestedPass && string.IsNullOrEmpty(failure) &&
                              allSegmentsPassed && restore.Passed &&
                              relevantRuntimeErrors.Count == 0;
                string reason = passed
                    ? string.Empty
                    : JoinFailureReasons(
                        failure,
                        allSegmentsPassed ? null : "Not all six measured segments passed.",
                        restore.Passed ? null : restore.Failure,
                        relevantRuntimeErrors.Count == 0
                            ? null
                            : string.Join(" | ", relevantRuntimeErrors));

                string outputFolder = string.Empty;
                string manifestSha = string.Empty;
                try
                {
                    ArtifactResult artifact = WriteArtifacts(passed, reason, restore);
                    outputFolder = artifact.OutputFolder;
                    manifestSha = artifact.ManifestSha;
                }
                catch (Exception exception)
                {
                    passed = false;
                    reason = JoinFailureReasons(reason, "Artifact write failed: " + exception);
                }

                string result = (passed ? "PASS " : "FAIL ") + Status + "\n" +
                                "sessionId=" + sessionId + "\n" +
                                "output=" + outputFolder + "\n" +
                                "segmentCount=" + segmentResults.Count + "\n" +
                                "recordCount=" + records.Count + "\n" +
                                "manifestSha256=" + manifestSha + "\n" +
                                "playModeToggled=false\n" +
                                "immediateApplyDuringMeasurement=false\n" +
                                "stateRestored=" + restore.Passed.ToString().ToLowerInvariant() +
                                (passed ? string.Empty : "\nfailure=" + reason);
                SessionCompleted(this, result);
                if (passed)
                    Debug.Log(result);
                else
                    Debug.LogError(result);
            }

            private RestoreReport RestoreState()
            {
                var failures = new List<string>();
                bool immediateApplied = false;
                TryRestoreStep(failures, "Start power", () =>
                    bindings.StartSwitcher.SetPowerLevel(originalStartPower));
                TryRestoreStep(failures, "Administrative power", () =>
                    bindings.AdministrativeSwitcher.SetPowerLevel(
                        originalAdministrativePower));
                TryRestoreStep(failures, "physical DoorLeaf rotation", () =>
                    bindings.DoorLeaf.localRotation = originalDoorRotation);
                TryRestoreStep(failures, "physics transform sync", Physics.SyncTransforms);
                TryRestoreStep(failures, "door-angle source", () =>
                    runtime.DoorAngleSource.EvaluateNow());
                TryRestoreStep(failures, "smoothing configuration", () =>
                    originalSmoothing.Restore(runtime.Connection));

                bool needsReactivation = Application.isPlaying &&
                    runtime.Manager != null && runtime.Connection != null &&
                    (!runtime.Manager.IsActive || runtime.Manager.IsFaultLatched ||
                     !runtime.Connection.IsTransportActive ||
                     runtime.Connection.IsFaultLatched);
                if (needsReactivation)
                {
                    TryRestoreStep(failures, "runtime deactivation", () =>
                        runtime.Manager.DeactivateAll());
                    TryRestoreStep(failures, "runtime fault reset", () =>
                        runtime.Manager.ResetFault());
                    TryRestoreStep(failures, "runtime reactivation", () =>
                    {
                        if (!runtime.Manager.TryActivateAll(out string activationFailure))
                            throw new InvalidOperationException(activationFailure);
                    });
                }
                if (Application.isPlaying && runtime.Connection != null &&
                    runtime.Connection.IsTransportActive)
                {
                    TryRestoreStep(failures, "final rollback apply", () =>
                    {
                        immediateApplied =
                            runtime.Connection.TryApplyCurrentInputsImmediately(
                                out string immediateFailure);
                        if (!immediateApplied)
                            throw new InvalidOperationException(immediateFailure);
                    });
                }
                TryRestoreStep(failures, "selection", () =>
                {
                    selection.Restore();
                    selection.AssertRestored();
                });

                try
                {
                    if (bindings.StartSwitcher.CurrentPowerLevel != originalStartPower ||
                        bindings.AdministrativeSwitcher.CurrentPowerLevel !=
                        originalAdministrativePower)
                        failures.Add("room power inputs were not restored.");
                    if (Quaternion.Angle(
                            bindings.DoorLeaf.localRotation,
                            originalDoorRotation) > 0.0001f)
                        failures.Add("physical DoorLeaf rotation was not restored.");
                    if (!Approximately(
                            ReadScalar(runtime.StartPower, "restored Start power"),
                            originalStartRaw) ||
                        !Approximately(
                            ReadScalar(
                                runtime.AdministrativePower,
                                "restored Administrative power"),
                            originalAdministrativeRaw) ||
                        !Approximately(
                            ReadScalar(runtime.DoorSource, "restored door aperture"),
                            originalDoorRaw))
                        failures.Add("raw scalar inputs were not restored.");
                    originalSmoothing.AssertRestored(runtime.Connection);
                    if (scene.IsValid() && scene.isLoaded &&
                        scene.isDirty != initialSceneDirty)
                        failures.Add("scene dirty state changed.");
                    string builtSha = KExactBasisV1EditorContract.ComputeFileSha256(
                        KExactBasisV1EditorContract.BuiltScenePath);
                    string buildManifestSha = KExactBasisV1EditorContract.ComputeFileSha256(
                        KExactBasisV1EditorContract.BuildManifestPath);
                    if (!string.Equals(
                            builtSha, initialBuiltSceneSha,
                            StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(
                            buildManifestSha, initialBuildManifestSha,
                            StringComparison.OrdinalIgnoreCase))
                        failures.Add("canonical scene or build manifest changed.");
                    protectedAssets.AssertUnchanged();
                }
                catch (Exception exception)
                {
                    failures.Add("rollback verification failed: " + exception);
                }

                return new RestoreReport(
                    failures.Count == 0,
                    string.Join(" | ", failures),
                    immediateApplied);
            }

            private static void TryRestoreStep(
                List<string> failures,
                string label,
                Action action)
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    failures.Add(label + " rollback failed: " + exception);
                }
            }

            private ArtifactResult WriteArtifacts(
                bool passed,
                string failure,
                RestoreReport restore)
            {
                string outputFolder = ResultsRoot + "/" + sessionId;
                string absoluteFolder =
                    KExactBasisV1EditorContract.AssetPathToAbsolutePath(outputFolder);
                Directory.CreateDirectory(absoluteFolder);
                string csvPath = Path.Combine(absoluteFolder, "transition_trace.csv");
                string manifestPath = Path.Combine(
                    absoluteFolder, "manifest_DYNAMIC_TRANSITION_TRACE.txt");

                File.WriteAllText(csvPath, BuildCsv(), new UTF8Encoding(false));
                string csvSha = ComputeFileSha256Absolute(csvPath);
                File.WriteAllText(
                    manifestPath,
                    BuildManifest(passed, failure, restore, outputFolder, csvSha),
                    new UTF8Encoding(false));
                return new ArtifactResult(
                    outputFolder,
                    ComputeFileSha256Absolute(manifestPath));
            }

            private string BuildCsv()
            {
                var builder = new StringBuilder(records.Count * 300);
                builder.AppendLine(
                    "schema,session_id,sequence,segment_id,callback_kind," +
                    "editor_time,unscaled_time,frame,target_from,target_to," +
                    "raw_start_power,raw_admin_power,raw_door_open," +
                    "raw_door_aperture,door_angle_degrees,smoothed_start_power," +
                    "smoothed_admin_power,smoothed_door_aperture,direct_a_to_b," +
                    "direct_b_to_a,bounce_a_to_b,bounce_b_to_a,reflection_a_to_b," +
                    "reflection_b_to_a,door_surface_a_to_b,door_surface_b_to_a," +
                    "manager_active,connection_active,fault_latched,fault_reason");
                for (int i = 0; i < records.Count; i++)
                    records[i].AppendCsv(builder, sessionId);
                return builder.ToString();
            }

            private string BuildManifest(
                bool passed,
                string failure,
                RestoreReport restore,
                string outputFolder,
                string csvSha)
            {
                var builder = new StringBuilder();
                builder.AppendLine("schema=" + Schema);
                builder.AppendLine("status=" + Status);
                builder.AppendLine("outcome=" + (passed ? "PASS" : "FAIL"));
                builder.AppendLine("sessionId=" + sessionId);
                builder.AppendLine("startedUtc=" + startedUtc.ToString("O"));
                builder.AppendLine("completedUtc=" + DateTime.UtcNow.ToString("O"));
                builder.AppendLine("outputFolder=" + outputFolder);
                builder.AppendLine(
                    "builtScenePath=" + KExactBasisV1EditorContract.BuiltScenePath);
                builder.AppendLine("builtSceneSha256=" + initialBuiltSceneSha);
                builder.AppendLine("buildManifestSha256=" + initialBuildManifestSha);
                builder.AppendLine("connectionKey=" + runtime.Connection.ConnectionKey);
                builder.AppendLine("powerRiseSeconds=0.35");
                builder.AppendLine("powerFallSeconds=0.5");
                builder.AppendLine("doorOpenSeconds=0.2");
                builder.AppendLine("doorCloseSeconds=0.2");
                builder.AppendLine("physicalDoorMotionSeconds=0.65");
                builder.AppendLine("convergenceTolerance=0.001");
                builder.AppendLine("playModeToggled=false");
                builder.AppendLine("playModeRestarted=false");
                builder.AppendLine("tryApplyImmediatelyDuringMeasurementCount=0");
                builder.AppendLine(
                    "tryApplyImmediatelyDuringFinalRestoreCount=" +
                    (restore.ImmediateApplied ? "1" : "0"));
                builder.AppendLine("weightsChangedSubscribed=true");
                builder.AppendLine("physicalDoorLeafDriven=true");
                builder.AppendLine("recordCount=" + records.Count);
                builder.AppendLine("segmentCount=" + segmentResults.Count);
                builder.AppendLine("runtimeRelevantErrorCount=" +
                                   relevantRuntimeErrors.Count);
                builder.AppendLine("stateRestored=" +
                                   restore.Passed.ToString().ToLowerInvariant());
                builder.AppendLine("selectionRestored=" +
                                   restore.Passed.ToString().ToLowerInvariant());
                builder.AppendLine("sceneDirtyStatePreserved=" +
                                   restore.Passed.ToString().ToLowerInvariant());
                builder.AppendLine("productionAssetsWritten=0");
                builder.AppendLine("visualParityClaimed=false");
                builder.AppendLine("transitionCsv=transition_trace.csv");
                builder.AppendLine("transitionCsvSha256=" + csvSha);
                for (int i = 0; i < segmentResults.Count; i++)
                    segmentResults[i].AppendManifest(builder);
                builder.AppendLine("failure=" +
                    KExactBasisV1EditorContract.Sanitize(failure));
                return builder.ToString();
            }
        }

        private sealed class RuntimeHandles
        {
            internal readonly KExactPortalRuntimeManager Manager;
            internal readonly KExactPortalConnection Connection;
            internal readonly KExactScalarSource StartPower;
            internal readonly KExactScalarSource AdministrativePower;
            internal readonly KExactScalarSource DoorSource;
            internal readonly DungeonPortalDoorAngleSource DoorAngleSource;

            internal RuntimeHandles(
                KExactPortalRuntimeManager manager,
                KExactPortalConnection connection,
                KExactScalarSource startPower,
                KExactScalarSource administrativePower,
                KExactScalarSource doorSource,
                DungeonPortalDoorAngleSource doorAngleSource)
            {
                Manager = manager;
                Connection = connection;
                StartPower = startPower;
                AdministrativePower = administrativePower;
                DoorSource = doorSource;
                DoorAngleSource = doorAngleSource;
            }

            internal void RequireStableActiveState()
            {
                bool parity = Connection.TryValidateParity(out string failure);
                if (!Manager.IsActive || Manager.IsFaultLatched ||
                    !string.IsNullOrEmpty(Manager.FaultReason) ||
                    !Connection.IsTransportActive || Connection.IsFaultLatched ||
                    !string.IsNullOrEmpty(Connection.FaultReason) || !parity)
                    throw new InvalidOperationException(
                        "K-exact runtime is not active/stable: " + failure);
            }
        }

        private static RuntimeHandles ResolveRuntimeHandles(
            KExactBasisV1EditorContract.SceneBindings bindings)
        {
            KExactPortalRuntimeManager[] managers =
                bindings.KExactRoot.GetComponentsInChildren<KExactPortalRuntimeManager>(true);
            KExactPortalConnection[] connections =
                bindings.KExactRoot.GetComponentsInChildren<KExactPortalConnection>(true);
            if (managers.Length != 1 || connections.Length != 1 ||
                !string.Equals(
                    connections[0].ConnectionKey,
                    ExpectedConnectionKey,
                    StringComparison.Ordinal) ||
                managers[0].Connections.Length != 1 ||
                managers[0].Connections[0] != connections[0])
                throw new InvalidOperationException(
                    "Canonical runtime manager/connection identity changed.");

            KExactPortalConnection connection = connections[0];
            KExactScalarSource start = connection.AToB?.SourcePower;
            KExactScalarSource administrative = connection.BToA?.SourcePower;
            KExactScalarSource door = connection.DoorOpennessSource;
            DungeonPortalDoorAngleSource angleSource =
                door?.SourceComponent as DungeonPortalDoorAngleSource;
            if (start == null || administrative == null || door == null ||
                angleSource == null || angleSource.DoorLeaf != bindings.DoorLeaf ||
                !angleSource.IsConfigured ||
                start.Mode != KExactScalarSource.SourceMode.PublicMember ||
                administrative.Mode != KExactScalarSource.SourceMode.PublicMember ||
                door.Mode != KExactScalarSource.SourceMode.PublicMember ||
                start.SourceComponent != bindings.StartSwitcher ||
                administrative.SourceComponent != bindings.AdministrativeSwitcher ||
                start.PublicMemberName !=
                    nameof(DungeonTileLightmapSwitcher.CurrentPowerLevel) ||
                administrative.PublicMemberName !=
                    nameof(DungeonTileLightmapSwitcher.CurrentPowerLevel) ||
                door.PublicMemberName !=
                    nameof(DungeonPortalDoorAngleSource.ApertureFraction) ||
                start.HasRuntimeOverride || administrative.HasRuntimeOverride ||
                door.HasRuntimeOverride ||
                connection.AToB.SelectedProductionLights.Length != 3 ||
                connection.BToA.SelectedProductionLights.Length != 2)
                throw new InvalidOperationException(
                    "Canonical scalar bindings changed or a capture override remains active.");

            var result = new RuntimeHandles(
                managers[0], connection, start, administrative, door, angleSource);
            result.RequireStableActiveState();
            return result;
        }

        private static Scene RequireRuntimeTraceScene()
        {
            if (!Application.isPlaying || !EditorApplication.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode !=
                EditorApplication.isPlaying || EditorApplication.isPaused)
                throw new InvalidOperationException(
                    "An already-running, stable, unpaused Play Mode is required; this " +
                    "tool never toggles it.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                Lightmapping.isRunning)
                throw new InvalidOperationException(
                    "Unity is compiling, updating, or lightmapping.");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Close Prefab Stage before tracing.");
            if (KExactBasisV1EditorContract.CountLoadedNonPreviewScenes() != 1 ||
                SceneManager.sceneCount != 1)
                throw new InvalidOperationException(
                    "Exactly one non-preview scene is required.");

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.isDirty ||
                !string.Equals(
                    KExactBasisV1EditorContract.NormalizePath(scene.path),
                    KExactBasisV1EditorContract.BuiltScenePath,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The exact clean canonical KExactBasisV1 scene must already be " +
                    "active in Play Mode.");
            KExactBasisV1EditorContract.ValidatePinnedInputs();
            string buildManifestPath =
                KExactBasisV1EditorContract.AssetPathToAbsolutePath(
                    KExactBasisV1EditorContract.BuildManifestPath);
            if (!File.Exists(buildManifestPath))
                throw new FileNotFoundException(
                    "Canonical K-exact build manifest is missing.", buildManifestPath);
            string buildManifest = File.ReadAllText(buildManifestPath);
            string declaredSceneSha = ReadUniqueManifestValue(
                buildManifest, "builtSceneSha256");
            string actualSceneSha = KExactBasisV1EditorContract.ComputeFileSha256(
                KExactBasisV1EditorContract.BuiltScenePath);
            if (!string.Equals(
                    declaredSceneSha, actualSceneSha,
                    StringComparison.OrdinalIgnoreCase) ||
                ReadUniqueManifestValue(buildManifest, "buildOutcome") != "COMPLETE" ||
                ReadUniqueManifestValue(buildManifest, "additionalRendererCount") != "0")
                throw new InvalidOperationException(
                    "Canonical scene/build-manifest binding is incomplete or stale.");
            return scene;
        }

        private static string ReadUniqueManifestValue(string text, string key)
        {
            string prefix = key + "=";
            string result = null;
            string[] lines = (text ?? string.Empty).Split(
                new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                if (result != null)
                    throw new InvalidOperationException(
                        "Duplicate build-manifest key: " + key);
                result = lines[i].Substring(prefix.Length);
            }
            if (result == null)
                throw new InvalidOperationException(
                    "Missing build-manifest key: " + key);
            return result;
        }

        private sealed class SmoothingSnapshot
        {
            private readonly float powerRise;
            private readonly float powerFall;
            private readonly float doorOpen;
            private readonly float doorClose;

            private SmoothingSnapshot(
                float powerRise,
                float powerFall,
                float doorOpen,
                float doorClose)
            {
                this.powerRise = powerRise;
                this.powerFall = powerFall;
                this.doorOpen = doorOpen;
                this.doorClose = doorClose;
            }

            internal static SmoothingSnapshot Capture(KExactPortalConnection connection)
            {
                var serialized = new SerializedObject(connection);
                serialized.UpdateIfRequiredOrScript();
                return new SmoothingSnapshot(
                    Read(serialized, "powerRiseSeconds"),
                    Read(serialized, "powerFallSeconds"),
                    Read(serialized, "doorOpenSeconds"),
                    Read(serialized, "doorCloseSeconds"));
            }

            internal void Restore(KExactPortalConnection connection)
            {
                connection.ConfigureSmoothing(
                    powerRise, powerFall, doorOpen, doorClose);
            }

            internal void AssertRestored(KExactPortalConnection connection)
            {
                SmoothingSnapshot current = Capture(connection);
                if (!Approximately(current.powerRise, powerRise) ||
                    !Approximately(current.powerFall, powerFall) ||
                    !Approximately(current.doorOpen, doorOpen) ||
                    !Approximately(current.doorClose, doorClose))
                    throw new InvalidOperationException(
                        "Connection smoothing configuration was not restored.");
            }

            private static float Read(SerializedObject serialized, string name)
            {
                SerializedProperty property = serialized.FindProperty(name);
                if (property == null || !float.IsFinite(property.floatValue))
                    throw new InvalidOperationException(
                        "Missing smoothing property: " + name);
                return property.floatValue;
            }
        }

        private sealed class DoorConfiguration
        {
            private readonly Quaternion closed;
            private readonly Vector3 axis;
            internal readonly float OpenAngleDegrees;

            private DoorConfiguration(Quaternion closed, Vector3 axis, float openAngle)
            {
                this.closed = closed;
                this.axis = axis;
                OpenAngleDegrees = openAngle;
            }

            internal static DoorConfiguration Capture(DungeonPortalDoorAngleSource source)
            {
                var serialized = new SerializedObject(source);
                serialized.UpdateIfRequiredOrScript();
                SerializedProperty closed = serialized.FindProperty("closedLocalRotation");
                SerializedProperty axis = serialized.FindProperty("localHingeAxis");
                SerializedProperty angle = serialized.FindProperty("openAngleDegrees");
                if (closed == null || axis == null || angle == null ||
                    axis.vector3Value.sqrMagnitude <= Mathf.Epsilon ||
                    Mathf.Abs(angle.floatValue - 90f) > 0.0001f)
                    throw new InvalidOperationException(
                        "Door configuration is unavailable or full-open is not 90 degrees.");
                return new DoorConfiguration(
                    closed.quaternionValue,
                    axis.vector3Value.normalized,
                    angle.floatValue);
            }

            internal Quaternion RotationFor(float fraction)
            {
                return closed * Quaternion.AngleAxis(
                    OpenAngleDegrees * Mathf.Clamp01(fraction), axis);
            }
        }

        private sealed class SegmentResult
        {
            internal readonly SegmentKind Kind;
            internal readonly float ExpectedStart;
            internal readonly float ExpectedEnd;
            internal readonly int StartFrame;
            internal readonly double StartTime;
            internal bool Passed { get; private set; }
            private int recordCount;
            private int weightsChangedCount;
            private int distinctWeightFrames;
            private double elapsed;

            internal bool IsStart =>
                Kind == SegmentKind.StartRise || Kind == SegmentKind.StartFall;
            internal bool IsAdministrative =>
                Kind == SegmentKind.AdministrativeRise ||
                Kind == SegmentKind.AdministrativeFall;

            internal SegmentResult(
                SegmentKind kind,
                float expectedStart,
                float expectedEnd,
                int startFrame,
                double startTime)
            {
                Kind = kind;
                ExpectedStart = expectedStart;
                ExpectedEnd = expectedEnd;
                StartFrame = startFrame;
                StartTime = startTime;
            }

            internal void Complete(
                int rows,
                int events,
                int eventFrames,
                double elapsedSeconds)
            {
                recordCount = rows;
                weightsChangedCount = events;
                distinctWeightFrames = eventFrames;
                elapsed = elapsedSeconds;
                Passed = true;
            }

            internal void AppendManifest(StringBuilder builder)
            {
                string prefix = "segment." + Kind + ".";
                builder.AppendLine(prefix + "outcome=" + (Passed ? "PASS" : "FAIL"));
                builder.AppendLine(prefix + "from=" + Format(ExpectedStart));
                builder.AppendLine(prefix + "to=" + Format(ExpectedEnd));
                builder.AppendLine(prefix + "recordCount=" + recordCount);
                builder.AppendLine(prefix + "weightsChangedCount=" + weightsChangedCount);
                builder.AppendLine(prefix + "distinctWeightsChangedFrames=" +
                                   distinctWeightFrames);
                builder.AppendLine(prefix + "elapsedUnscaledSeconds=" +
                                   Format(elapsed));
                builder.AppendLine(prefix + "monotonic=" +
                                   Passed.ToString().ToLowerInvariant());
                builder.AppendLine(prefix + "noOvershoot=" +
                                   Passed.ToString().ToLowerInvariant());
                builder.AppendLine(prefix + "multiFrame=" +
                                   Passed.ToString().ToLowerInvariant());
                builder.AppendLine(prefix + "convergedWithin1e-3=" +
                                   Passed.ToString().ToLowerInvariant());
                builder.AppendLine(prefix + "faultFree=" +
                                   Passed.ToString().ToLowerInvariant());
            }
        }

        private readonly struct TraceRecord
        {
            internal readonly int Sequence;
            internal readonly string SegmentId;
            internal readonly string CallbackKind;
            internal readonly double EditorTime;
            internal readonly double UnscaledTime;
            internal readonly int Frame;
            internal readonly float TargetFrom;
            internal readonly float TargetTo;
            internal readonly float RawStart;
            internal readonly float RawAdministrative;
            internal readonly float RawDoorOpen;
            internal readonly float RawDoorAperture;
            internal readonly float DoorAngleDegrees;
            internal readonly float SmoothedStart;
            internal readonly float SmoothedAdministrative;
            internal readonly float SmoothedDoorAperture;
            internal readonly float DirectAToB;
            internal readonly float DirectBToA;
            internal readonly float BounceAToB;
            internal readonly float BounceBToA;
            internal readonly float ReflectionAToB;
            internal readonly float ReflectionBToA;
            internal readonly float DoorSurfaceAToB;
            internal readonly float DoorSurfaceBToA;
            internal readonly bool ManagerActive;
            internal readonly bool ConnectionActive;
            internal readonly bool FaultLatched;
            internal readonly string FaultReason;

            internal bool AllTelemetryFiniteAndNonNegative =>
                IsFinite01(RawStart) && IsFinite01(RawAdministrative) &&
                IsFinite01(RawDoorOpen) && IsFinite01(RawDoorAperture) &&
                IsFiniteNonNegative(DoorAngleDegrees) && IsFinite01(SmoothedStart) &&
                IsFinite01(SmoothedAdministrative) &&
                IsFinite01(SmoothedDoorAperture) &&
                IsFiniteNonNegative(DirectAToB) && IsFiniteNonNegative(DirectBToA) &&
                IsFiniteNonNegative(BounceAToB) && IsFiniteNonNegative(BounceBToA) &&
                IsFiniteNonNegative(ReflectionAToB) &&
                IsFiniteNonNegative(ReflectionBToA) &&
                IsFiniteNonNegative(DoorSurfaceAToB) &&
                IsFiniteNonNegative(DoorSurfaceBToA);

            internal TraceRecord(
                int sequence,
                string segmentId,
                string callbackKind,
                double editorTime,
                double unscaledTime,
                int frame,
                float targetFrom,
                float targetTo,
                float rawStart,
                float rawAdministrative,
                float rawDoorOpen,
                float rawDoorAperture,
                float doorAngleDegrees,
                float smoothedStart,
                float smoothedAdministrative,
                float smoothedDoorAperture,
                float directAToB,
                float directBToA,
                float bounceAToB,
                float bounceBToA,
                float reflectionAToB,
                float reflectionBToA,
                float doorSurfaceAToB,
                float doorSurfaceBToA,
                bool managerActive,
                bool connectionActive,
                bool faultLatched,
                string faultReason)
            {
                Sequence = sequence;
                SegmentId = segmentId;
                CallbackKind = callbackKind;
                EditorTime = editorTime;
                UnscaledTime = unscaledTime;
                Frame = frame;
                TargetFrom = targetFrom;
                TargetTo = targetTo;
                RawStart = rawStart;
                RawAdministrative = rawAdministrative;
                RawDoorOpen = rawDoorOpen;
                RawDoorAperture = rawDoorAperture;
                DoorAngleDegrees = doorAngleDegrees;
                SmoothedStart = smoothedStart;
                SmoothedAdministrative = smoothedAdministrative;
                SmoothedDoorAperture = smoothedDoorAperture;
                DirectAToB = directAToB;
                DirectBToA = directBToA;
                BounceAToB = bounceAToB;
                BounceBToA = bounceBToA;
                ReflectionAToB = reflectionAToB;
                ReflectionBToA = reflectionBToA;
                DoorSurfaceAToB = doorSurfaceAToB;
                DoorSurfaceBToA = doorSurfaceBToA;
                ManagerActive = managerActive;
                ConnectionActive = connectionActive;
                FaultLatched = faultLatched;
                FaultReason = faultReason ?? string.Empty;
            }

            internal void AppendCsv(StringBuilder builder, string sessionId)
            {
                AppendCsvValue(builder, Schema);
                AppendCsvValue(builder, sessionId);
                AppendCsvValue(builder, Sequence);
                AppendCsvValue(builder, SegmentId);
                AppendCsvValue(builder, CallbackKind);
                AppendCsvValue(builder, EditorTime);
                AppendCsvValue(builder, UnscaledTime);
                AppendCsvValue(builder, Frame);
                AppendCsvValue(builder, TargetFrom);
                AppendCsvValue(builder, TargetTo);
                AppendCsvValue(builder, RawStart);
                AppendCsvValue(builder, RawAdministrative);
                AppendCsvValue(builder, RawDoorOpen);
                AppendCsvValue(builder, RawDoorAperture);
                AppendCsvValue(builder, DoorAngleDegrees);
                AppendCsvValue(builder, SmoothedStart);
                AppendCsvValue(builder, SmoothedAdministrative);
                AppendCsvValue(builder, SmoothedDoorAperture);
                AppendCsvValue(builder, DirectAToB);
                AppendCsvValue(builder, DirectBToA);
                AppendCsvValue(builder, BounceAToB);
                AppendCsvValue(builder, BounceBToA);
                AppendCsvValue(builder, ReflectionAToB);
                AppendCsvValue(builder, ReflectionBToA);
                AppendCsvValue(builder, DoorSurfaceAToB);
                AppendCsvValue(builder, DoorSurfaceBToA);
                AppendCsvValue(builder, ManagerActive);
                AppendCsvValue(builder, ConnectionActive);
                AppendCsvValue(builder, FaultLatched);
                AppendCsvValue(builder, FaultReason, true);
                builder.AppendLine();
            }
        }

        private readonly struct RestoreReport
        {
            internal readonly bool Passed;
            internal readonly string Failure;
            internal readonly bool ImmediateApplied;

            internal RestoreReport(bool passed, string failure, bool immediateApplied)
            {
                Passed = passed;
                Failure = failure ?? string.Empty;
                ImmediateApplied = immediateApplied;
            }
        }

        private readonly struct ArtifactResult
        {
            internal readonly string OutputFolder;
            internal readonly string ManifestSha;

            internal ArtifactResult(string outputFolder, string manifestSha)
            {
                OutputFolder = outputFolder;
                ManifestSha = manifestSha;
            }
        }

        private static float ReadScalar(KExactScalarSource source, string label)
        {
            float value = 0f;
            string failure = source == null ? "scalar source is missing" : string.Empty;
            if (source == null || !source.TryRead01(out value, out failure) ||
                !float.IsFinite(value))
                throw new InvalidOperationException(label + " read failed: " + failure);
            return value;
        }

        private static bool Approximately(float left, float right)
        {
            return Mathf.Abs(left - right) <= 0.001f;
        }

        private static bool ApproximatelySettled(float left, float right)
        {
            // KExactPortalConnection snaps to the target and clears SmoothDamp velocity
            // below 1e-4. Waiting through that boundary avoids carrying rise velocity
            // into the immediately following fall segment.
            return Mathf.Abs(left - right) <= 0.00011f;
        }

        private static bool IsFinite01(float value)
        {
            return float.IsFinite(value) && value >= -0.0001f && value <= 1.0001f;
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return float.IsFinite(value) && value >= -0.0001f;
        }

        private static string JoinFailureReasons(params string[] reasons)
        {
            return string.Join(
                " | ",
                reasons.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string ComputeFileSha256Absolute(string absolutePath)
        {
            using (FileStream stream = File.OpenRead(absolutePath))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(stream);
                var builder = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static string Format(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Format(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void AppendCsvValue(StringBuilder builder, object value, bool last = false)
        {
            string text;
            if (value is float single)
                text = Format(single);
            else if (value is double number)
                text = Format(number);
            else if (value is bool boolean)
                text = boolean ? "true" : "false";
            else if (value is IFormattable formattable)
                text = formattable.ToString(null, CultureInfo.InvariantCulture);
            else
                text = value?.ToString() ?? string.Empty;
            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
                text = "\"" + text.Replace("\"", "\"\"") + "\"";
            builder.Append(text);
            if (!last)
                builder.Append(',');
        }
    }
}
