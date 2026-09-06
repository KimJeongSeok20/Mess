using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonPortalTransportPoC.Editor
{
    /// <summary>
    /// Captures the isolated Start/Admin Play-mode SMOKE matrix without taking control of
    /// Play Mode, the user's camera, or the transport connection.  The two serialized,
    /// disabled validation cameras render into tool-owned targets and are restored after
    /// every image.  This is evidence capture only: it never declares visual parity.
    /// </summary>
    public static class DungeonPortalTransportSmokeCapture
    {
        public const string ValidationScenePath =
            "Assets/Experiments/DungeonPortalTransportPoC/Scenes/Start_Admin_PortalTransportValidation.unity";
        public const string EvidenceRoot =
            "Assets/Experiments/DungeonPortalTransportPoC/Evidence/Smoke";
        public const int ExpectedStateCount = 12;
        public const int ExpectedImageCount = 24;

        private const string StartRoomId = "StartRoom_R000";
        private const string AdminRoomId = "AdminstrativeSegregation_R000";
        private const string StartEndpointName = "StartRoom_Endpoint_ProfilePending";
        private const string AdminEndpointName = "AdminRoom_Endpoint_ProfilePending";
        private const string ConnectionName = "A_to_B_and_B_to_A_Connection_ProfileGate_DISABLED";
        private const string CameraParentName = "03_FixedValidationCameras_DISABLED";
        private const string StartCameraName = "Start_to_Admin_FixedCamera_DISABLED";
        private const string AdminCameraName = "Admin_to_Start_FixedCamera_DISABLED";
        private const int CaptureWidth = 1920;
        private const int CaptureHeight = 1080;
        private const int MinimumSettleFrames = 2;
        private const string ToolVersion = "DungeonPortalTransportSmokeCapture.v1";

        private static CaptureRunner activeRunner;

        [MenuItem(
            "Tools/Dungeon/Lighting/Portal Transport PoC/Capture 24-Image SMOKE Matrix (Play Mode Only)")]
        public static void CaptureFromMenu()
        {
            string result = StartCapture();
            if (result.StartsWith("STARTED", StringComparison.Ordinal))
                Debug.Log(result);
            else
                Debug.LogError(result);
        }

        [MenuItem(
            "Tools/Dungeon/Lighting/Portal Transport PoC/Capture 24-Image SMOKE Matrix (Play Mode Only)",
            true)]
        private static bool ValidateCaptureMenu()
        {
            return activeRunner == null &&
                   EditorApplication.isPlaying &&
                   !EditorApplication.isPaused &&
                   !EditorApplication.isCompiling &&
                   !EditorApplication.isUpdating &&
                   !Lightmapping.isRunning;
        }

        /// <summary>
        /// Starts an asynchronous capture.  It deliberately refuses to enter/exit Play
        /// Mode, load/save a scene, enable/disable the connection, or enable a camera.
        /// </summary>
        public static string StartCapture()
        {
            if (activeRunner != null)
                return "FAIL: a portal-transport SMOKE capture is already running.";

            if (!TryCreateRunner(out CaptureRunner runner, out string failure))
                return "FAIL: " + failure;

            activeRunner = runner;
            runner.Begin();
            return "STARTED portal-transport SMOKE capture. output=" + runner.OutputAssetFolder +
                   " expectedStates=" + ExpectedStateCount +
                   " expectedImages=" + ExpectedImageCount +
                   " classification=" + runner.Classification;
        }

        private static bool TryCreateRunner(
            out CaptureRunner runner,
            out string failure)
        {
            runner = null;
            failure = string.Empty;

            if (!EditorApplication.isPlaying || !Application.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode == false)
            {
                failure = "the exact validation scene must already be running in Play Mode; this tool never enters Play Mode.";
                return false;
            }

            if (EditorApplication.isPaused)
            {
                failure = "Play Mode is paused, so two real runtime settle frames cannot be proven.";
                return false;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                failure = "Unity is compiling or updating assets.";
                return false;
            }

            if (Lightmapping.isRunning)
            {
                failure = "Lightmapping is running.";
                return false;
            }

            if (SceneManager.sceneCount != 1)
            {
                failure = "exactly one loaded runtime scene is required; loadedSceneCount=" +
                          SceneManager.sceneCount.ToString(CultureInfo.InvariantCulture) + ".";
                return false;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded ||
                !string.Equals(scene.path, ValidationScenePath, StringComparison.Ordinal))
            {
                failure = "the active Play-mode scene is not the exact isolated validation scene. active='" +
                          scene.path + "'.";
                return false;
            }

            if (scene.isDirty)
            {
                failure = "the validation scene is dirty; no evidence was captured or written.";
                return false;
            }

            try
            {
                DungeonPortalEndpoint startEndpoint = FindUniqueNamedComponent<DungeonPortalEndpoint>(
                    scene,
                    StartEndpointName);
                DungeonPortalEndpoint adminEndpoint = FindUniqueNamedComponent<DungeonPortalEndpoint>(
                    scene,
                    AdminEndpointName);
                DungeonPortalPowerEnvelope startPower =
                    RequireSameObjectComponent<DungeonPortalPowerEnvelope>(startEndpoint);
                DungeonPortalPowerEnvelope adminPower =
                    RequireSameObjectComponent<DungeonPortalPowerEnvelope>(adminEndpoint);
                DungeonPortalTransportConnection connection =
                    FindUniqueNamedComponent<DungeonPortalTransportConnection>(scene, ConnectionName);
                DungeonPortalDoorAngleSource doorSource =
                    RequireSameObjectComponent<DungeonPortalDoorAngleSource>(connection);

                Camera startCamera = FindUniqueNamedComponent<Camera>(scene, StartCameraName);
                Camera adminCamera = FindUniqueNamedComponent<Camera>(scene, AdminCameraName);
                AssertFixedCamera(startCamera);
                AssertFixedCamera(adminCamera);

                if (startPower == adminPower)
                    throw new InvalidOperationException("Start/Admin resolved the same power envelope.");
                if (!Approximately(startPower.Power01, startPower.TargetPower01) ||
                    !Approximately(adminPower.Power01, adminPower.TargetPower01))
                {
                    throw new InvalidOperationException(
                        "Both room power envelopes must be stable before capture; " +
                        "an in-progress transition phase cannot be restored exactly.");
                }
                if (startCamera == adminCamera)
                    throw new InvalidOperationException("Start/Admin resolved the same fixed camera.");
                if (!doorSource.IsConfigured || doorSource.DoorLeaf == null)
                    throw new InvalidOperationException("The canonical door angle source is not configured.");

                ReadDoorConfiguration(
                    doorSource,
                    out Quaternion closedLocalRotation,
                    out Vector3 localHingeAxis,
                    out float openAngleDegrees);

                string outputAssetFolder = CreateUniqueOutputFolder();
                runner = new CaptureRunner(
                    scene,
                    outputAssetFolder,
                    startEndpoint,
                    adminEndpoint,
                    startPower,
                    adminPower,
                    connection,
                    doorSource,
                    closedLocalRotation,
                    localHingeAxis,
                    openAngleDegrees,
                    startCamera,
                    adminCamera,
                    HandleRunnerFinished);
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        private static void HandleRunnerFinished(CaptureRunner runner)
        {
            if (ReferenceEquals(activeRunner, runner))
                activeRunner = null;
        }

        private static T FindUniqueNamedComponent<T>(Scene scene, string objectName)
            where T : Component
        {
            T result = null;
            int count = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                T[] candidates = roots[rootIndex].GetComponentsInChildren<T>(true);
                for (int i = 0; i < candidates.Length; i++)
                {
                    T candidate = candidates[i];
                    if (candidate == null ||
                        !string.Equals(candidate.gameObject.name, objectName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    count++;
                    result = candidate;
                }
            }

            if (count != 1 || result == null)
            {
                throw new InvalidOperationException(
                    "Expected exactly one '" + objectName + "' with component " +
                    typeof(T).Name + ", found " + count.ToString(CultureInfo.InvariantCulture) + ".");
            }

            return result;
        }

        private static T RequireSameObjectComponent<T>(Component owner)
            where T : Component
        {
            T result = owner != null ? owner.GetComponent<T>() : null;
            if (result == null)
            {
                throw new InvalidOperationException(
                    "'" + (owner != null ? owner.gameObject.name : "null") +
                    "' is missing required component " + typeof(T).Name + ".");
            }

            return result;
        }

        private static void AssertFixedCamera(Camera camera)
        {
            if (camera.enabled)
                throw new InvalidOperationException("Fixed camera '" + camera.name + "' must remain disabled.");
            if (!camera.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Fixed camera '" + camera.name + "' is not active in the hierarchy.");
            if (camera.transform.parent == null ||
                !string.Equals(camera.transform.parent.name, CameraParentName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Fixed camera '" + camera.name + "' is outside the canonical camera container.");
            }
        }

        private static void ReadDoorConfiguration(
            DungeonPortalDoorAngleSource source,
            out Quaternion closedLocalRotation,
            out Vector3 localHingeAxis,
            out float openAngleDegrees)
        {
            var serialized = new SerializedObject(source);
            serialized.UpdateIfRequiredOrScript();
            SerializedProperty closed = serialized.FindProperty("closedLocalRotation");
            SerializedProperty axis = serialized.FindProperty("localHingeAxis");
            SerializedProperty angle = serialized.FindProperty("openAngleDegrees");
            if (closed == null || axis == null || angle == null)
                throw new InvalidOperationException("Canonical door configuration fields could not be read.");

            closedLocalRotation = closed.quaternionValue;
            localHingeAxis = axis.vector3Value;
            openAngleDegrees = angle.floatValue;
            if (localHingeAxis.sqrMagnitude <= Mathf.Epsilon ||
                Mathf.Abs(openAngleDegrees) <= 0.001f)
            {
                throw new InvalidOperationException("Canonical door hinge axis/open angle is invalid.");
            }

            localHingeAxis.Normalize();
        }

        private static string CreateUniqueOutputFolder()
        {
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff'Z'", CultureInfo.InvariantCulture);
            string assetFolder = EvidenceRoot + "/" + stamp;
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new InvalidOperationException("Unable to resolve the Unity project root.");

            string absoluteEvidenceRoot = Path.GetFullPath(Path.Combine(projectRoot, EvidenceRoot));
            string absoluteOutput = Path.GetFullPath(Path.Combine(projectRoot, assetFolder));
            string rootedPrefix = absoluteEvidenceRoot.TrimEnd(Path.DirectorySeparatorChar) +
                                  Path.DirectorySeparatorChar;
            if (!absoluteOutput.StartsWith(rootedPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Resolved SMOKE output escaped the isolated evidence root.");
            if (Directory.Exists(absoluteOutput))
                throw new InvalidOperationException("Refusing to reuse existing SMOKE output '" + assetFolder + "'.");

            return assetFolder.Replace('\\', '/');
        }

        private sealed class CaptureRunner
        {
            private readonly Scene scene;
            private readonly string outputAbsoluteFolder;
            private readonly DungeonPortalEndpoint startEndpoint;
            private readonly DungeonPortalEndpoint adminEndpoint;
            private readonly DungeonPortalPowerEnvelope startPower;
            private readonly DungeonPortalPowerEnvelope adminPower;
            private readonly DungeonPortalTransportConnection connection;
            private readonly DungeonPortalDoorAngleSource doorSource;
            private readonly Transform doorLeaf;
            private readonly Quaternion closedLocalRotation;
            private readonly Vector3 localHingeAxis;
            private readonly float openAngleDegrees;
            private readonly CameraSnapshot[] cameraSnapshots;
            private readonly PowerSnapshot startPowerSnapshot;
            private readonly PowerSnapshot adminPowerSnapshot;
            private readonly Quaternion originalDoorLocalRotation;
            private readonly bool connectionActiveSnapshot;
            private readonly bool startEndpointActiveSnapshot;
            private readonly bool adminEndpointActiveSnapshot;
            private readonly Action<CaptureRunner> finishedCallback;
            private readonly SmokeManifest manifest;
            private readonly StateDefinition[] states;
            private readonly List<PendingFile> pendingFiles = new List<PendingFile>();

            private RenderTexture renderTarget;
            private RenderTexture readbackTarget;
            private bool subscribed;
            private bool finished;
            private bool waitingForSettle;
            private int currentStateIndex = -1;
            private int appliedFrame;

            public CaptureRunner(
                Scene scene,
                string outputAssetFolder,
                DungeonPortalEndpoint startEndpoint,
                DungeonPortalEndpoint adminEndpoint,
                DungeonPortalPowerEnvelope startPower,
                DungeonPortalPowerEnvelope adminPower,
                DungeonPortalTransportConnection connection,
                DungeonPortalDoorAngleSource doorSource,
                Quaternion closedLocalRotation,
                Vector3 localHingeAxis,
                float openAngleDegrees,
                Camera startCamera,
                Camera adminCamera,
                Action<CaptureRunner> finishedCallback)
            {
                this.scene = scene;
                OutputAssetFolder = outputAssetFolder;
                outputAbsoluteFolder = AssetPathToAbsolutePath(outputAssetFolder);
                this.startEndpoint = startEndpoint;
                this.adminEndpoint = adminEndpoint;
                this.startPower = startPower;
                this.adminPower = adminPower;
                this.connection = connection;
                this.doorSource = doorSource;
                doorLeaf = doorSource.DoorLeaf;
                this.closedLocalRotation = closedLocalRotation;
                this.localHingeAxis = localHingeAxis;
                this.openAngleDegrees = openAngleDegrees;
                this.finishedCallback = finishedCallback;

                startPowerSnapshot = new PowerSnapshot(startPower);
                adminPowerSnapshot = new PowerSnapshot(adminPower);
                originalDoorLocalRotation = doorLeaf.localRotation;
                connectionActiveSnapshot = connection.isActiveAndEnabled;
                startEndpointActiveSnapshot = startEndpoint.IsConnectionActive;
                adminEndpointActiveSnapshot = adminEndpoint.IsConnectionActive;
                cameraSnapshots = new[]
                {
                    new CameraSnapshot("StartToAdmin", startCamera),
                    new CameraSnapshot("AdminToStart", adminCamera)
                };
                states = BuildStates();

                manifest = BuildManifest();
                Classification = manifest.status;
            }

            public string OutputAssetFolder { get; }
            public string Classification { get; }

            public void Begin()
            {
                EditorApplication.update += Update;
                EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
                AssemblyReloadEvents.beforeAssemblyReload += HandleBeforeAssemblyReload;
                subscribed = true;
            }

            private SmokeManifest BuildManifest()
            {
                bool connectionComponentActive = connection != null && connection.isActiveAndEnabled;
                bool endpointsActive = startEndpoint != null && adminEndpoint != null &&
                                       startEndpoint.IsConnectionActive && adminEndpoint.IsConnectionActive;
                var readinessReasons = new List<string>();
                if (!connectionComponentActive)
                    readinessReasons.Add("Transport connection is not active; the tool did not enable it.");
                if (!endpointsActive)
                    readinessReasons.Add("Both endpoints are not claimed by an active transport connection.");

                EndpointEvidence startEvidence = BuildEndpointEvidence("Start", startEndpoint);
                EndpointEvidence adminEvidence = BuildEndpointEvidence("Admin", adminEndpoint);
                AppendEndpointReadiness(startEvidence, readinessReasons);
                AppendEndpointReadiness(adminEvidence, readinessReasons);

                bool accepted = connectionComponentActive && endpointsActive &&
                                startEvidence.acceptedReceiverEvidence &&
                                adminEvidence.acceptedReceiverEvidence;
                string status;
                if (!connectionComponentActive || !endpointsActive)
                    status = "BLOCKED";
                else if (!accepted)
                    status = "BASELINE";
                else
                    status = "SMOKE_CANDIDATE";

                readinessReasons.Add(
                    "No capture produced by this tool establishes visual acceptance; BAKE-versus-REALTIME equivalence still requires comparison and human review.");
                readinessReasons.Add("Rejected V21 evidence and renderer-overlay paths are not used by this capture tool.");

                return new SmokeManifest
                {
                    schemaVersion = 1,
                    toolVersion = ToolVersion,
                    status = status,
                    outcome = "RUNNING",
                    startedUtcIso8601 = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    completedUtcIso8601 = string.Empty,
                    unityVersion = Application.unityVersion,
                    sceneAssetPath = scene.path,
                    sceneGuid = AssetDatabase.AssetPathToGUID(scene.path),
                    sceneDependencyHash = AssetDatabase.GetAssetDependencyHash(scene.path).ToString(),
                    outputAssetFolder = OutputAssetFolder,
                    width = CaptureWidth,
                    height = CaptureHeight,
                    minimumRuntimeSettleFrames = MinimumSettleFrames,
                    expectedStateCount = ExpectedStateCount,
                    expectedImageCount = ExpectedImageCount,
                    capturedStateCount = 0,
                    capturedImageCount = 0,
                    playModeOwnedByTool = false,
                    sceneOpenedOrSavedByTool = false,
                    connectionMutatedByTool = false,
                    camerasEnabledByTool = false,
                    userCameraOrSelectionMutatedByTool = false,
                    realtimeReferenceCaptured = false,
                    visualParityClaimed = false,
                    connectionComponentActiveAtStart = connectionComponentActive,
                    bothEndpointsConnectionActiveAtStart = endpointsActive,
                    startEndpoint = startEvidence,
                    adminEndpoint = adminEvidence,
                    readinessReasons = readinessReasons.ToArray(),
                    states = Array.Empty<StateEvidence>(),
                    restoration = new RestorationEvidence()
                };
            }

            private void Update()
            {
                if (finished)
                    return;

                try
                {
                    AssertRuntimeStillSafe();
                    if (!waitingForSettle)
                    {
                        currentStateIndex++;
                        if (currentStateIndex >= states.Length)
                        {
                            Finish(null);
                            return;
                        }

                        ApplyState(states[currentStateIndex]);
                        appliedFrame = Time.frameCount;
                        waitingForSettle = true;
                        return;
                    }

                    int settledFrames = Time.frameCount - appliedFrame;
                    if (settledFrames < MinimumSettleFrames)
                        return;

                    CaptureState(states[currentStateIndex], settledFrames);
                    waitingForSettle = false;
                }
                catch (Exception exception)
                {
                    Finish(exception);
                }
            }

            private void AssertRuntimeStillSafe()
            {
                if (!EditorApplication.isPlaying || !Application.isPlaying ||
                    EditorApplication.isPlayingOrWillChangePlaymode == false)
                {
                    throw new InvalidOperationException("Play Mode ended or began changing during capture.");
                }
                if (EditorApplication.isPaused)
                    throw new InvalidOperationException("Play Mode was paused during capture.");
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    throw new InvalidOperationException("Unity began compiling or updating assets during capture.");
                if (Lightmapping.isRunning)
                    throw new InvalidOperationException("Lightmapping began during capture.");
                if (!scene.IsValid() || !scene.isLoaded || SceneManager.GetActiveScene() != scene ||
                    SceneManager.sceneCount != 1 ||
                    !string.Equals(scene.path, ValidationScenePath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The exact isolated validation scene stopped being the sole active scene.");
                }
                if (scene.isDirty)
                    throw new InvalidOperationException("The validation scene became dirty during capture.");
                if (doorLeaf == null || startPower == null || adminPower == null ||
                    doorSource == null || connection == null)
                {
                    throw new InvalidOperationException("A required runtime object was destroyed during capture.");
                }
                if (connection.isActiveAndEnabled != connectionActiveSnapshot ||
                    startEndpoint.IsConnectionActive != startEndpointActiveSnapshot ||
                    adminEndpoint.IsConnectionActive != adminEndpointActiveSnapshot)
                {
                    throw new InvalidOperationException(
                        "Connection/endpoint active state changed during capture; the tool did not request that change.");
                }
                for (int i = 0; i < cameraSnapshots.Length; i++)
                {
                    if (cameraSnapshots[i].Camera == null || cameraSnapshots[i].Camera.enabled)
                        throw new InvalidOperationException("A fixed validation camera was destroyed or enabled during capture.");
                }
            }

            private void ApplyState(StateDefinition state)
            {
                startPower.SetImmediate(state.startPower01);
                adminPower.SetImmediate(state.adminPower01);
                doorLeaf.localRotation = closedLocalRotation * Quaternion.AngleAxis(
                    openAngleDegrees * state.doorOpenFraction,
                    localHingeAxis);
                doorSource.EvaluateNow();
            }

            private void CaptureState(StateDefinition state, int settledFrames)
            {
                doorSource.EvaluateNow();
                AssertApproximately(startPower.Power01, state.startPower01, "Start actual power");
                AssertApproximately(startPower.TargetPower01, state.startPower01, "Start target power");
                AssertApproximately(adminPower.Power01, state.adminPower01, "Admin actual power");
                AssertApproximately(adminPower.TargetPower01, state.adminPower01, "Admin target power");
                AssertApproximately(
                    doorSource.OpenFraction,
                    state.doorOpenFraction,
                    "Door OpenFraction",
                    0.002f);

                EnsureRenderTargets();
                var images = new ImageEvidence[cameraSnapshots.Length];
                for (int i = 0; i < cameraSnapshots.Length; i++)
                    images[i] = CaptureCamera(state, cameraSnapshots[i]);

                var stateEvidence = new StateEvidence
                {
                    stateId = state.stateId,
                    requestedStartPower01 = state.startPower01,
                    requestedAdminPower01 = state.adminPower01,
                    requestedDoorDegrees = state.doorDegrees,
                    requestedDoorOpenFraction = state.doorOpenFraction,
                    actualStartPower01 = startPower.Power01,
                    actualStartTargetPower01 = startPower.TargetPower01,
                    actualAdminPower01 = adminPower.Power01,
                    actualAdminTargetPower01 = adminPower.TargetPower01,
                    actualDoorOpenFraction = doorSource.OpenFraction,
                    actualApertureFraction = doorSource.ApertureFraction,
                    actualDoorLocalRotation = FloatVector.FromQuaternion(doorLeaf.localRotation),
                    appliedRuntimeFrame = appliedFrame,
                    capturedRuntimeFrame = Time.frameCount,
                    settledRuntimeFrames = settledFrames,
                    connectionComponentActive = connection.isActiveAndEnabled,
                    startEndpointConnectionActive = startEndpoint.IsConnectionActive,
                    adminEndpointConnectionActive = adminEndpoint.IsConnectionActive,
                    images = images
                };
                AppendState(stateEvidence);
            }

            private ImageEvidence CaptureCamera(
                StateDefinition state,
                CameraSnapshot snapshot)
            {
                Camera camera = snapshot.Camera;
                if (camera.enabled)
                    throw new InvalidOperationException("Refusing to render enabled fixed camera '" + camera.name + "'.");

                RenderTexture previousActive = RenderTexture.active;
                Texture2D readback = null;
                try
                {
                    camera.targetTexture = renderTarget;
                    camera.aspect = CaptureWidth / (float)CaptureHeight;
                    camera.ResetProjectionMatrix();
                    renderTarget.DiscardContents();
                    camera.Render();
                    Graphics.Blit(renderTarget, readbackTarget);

                    RenderTexture.active = readbackTarget;
                    readback = new Texture2D(
                        CaptureWidth,
                        CaptureHeight,
                        TextureFormat.RGBA32,
                        false,
                        false)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    readback.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0, false);
                    readback.Apply(false, false);
                    byte[] png = readback.EncodeToPNG();
                    if (png == null || png.Length == 0)
                        throw new InvalidOperationException("PNG encoding returned no bytes for '" + camera.name + "'.");

                    string filename = state.stateId + "_" + snapshot.CameraId + ".png";
                    Matrix4x4 projection = camera.projectionMatrix;
                    var evidence = new ImageEvidence
                    {
                        cameraId = snapshot.CameraId,
                        cameraName = camera.name,
                        assetPath = OutputAssetFolder + "/" + filename,
                        sha256 = ComputeSha256(png),
                        byteCount = png.LongLength,
                        cameraRemainedDisabled = true,
                        cameraPosition = FloatVector.FromVector3(camera.transform.position),
                        cameraRotation = FloatVector.FromQuaternion(camera.transform.rotation),
                        cameraLocalToWorldMatrix = FloatMatrix.From(camera.transform.localToWorldMatrix),
                        worldToCameraMatrix = FloatMatrix.From(camera.worldToCameraMatrix),
                        projectionMatrix = FloatMatrix.From(projection),
                        gpuProjectionMatrix = FloatMatrix.From(GL.GetGPUProjectionMatrix(projection, true)),
                        aspect = camera.aspect,
                        fieldOfView = camera.fieldOfView,
                        orthographic = camera.orthographic,
                        orthographicSize = camera.orthographicSize,
                        nearClipPlane = camera.nearClipPlane,
                        farClipPlane = camera.farClipPlane,
                        cullingMask = camera.cullingMask,
                        allowHdr = camera.allowHDR,
                        allowMsaa = camera.allowMSAA,
                        renderTargetFormat = renderTarget.format.ToString(),
                        renderTargetAntiAliasing = renderTarget.antiAliasing
                    };
                    pendingFiles.Add(new PendingFile(filename, png));
                    return evidence;
                }
                finally
                {
                    RenderTexture.active = previousActive;
                    if (readback != null)
                        UnityEngine.Object.DestroyImmediate(readback);
                    snapshot.Restore();
                }
            }

            private void EnsureRenderTargets()
            {
                if (renderTarget != null && readbackTarget != null)
                    return;

                int requestedMsaa = 1;
                if (cameraSnapshots[0].Camera.allowMSAA && cameraSnapshots[1].Camera.allowMSAA)
                    requestedMsaa = Mathf.Max(1, QualitySettings.antiAliasing);
                if (requestedMsaa != 1 && requestedMsaa != 2 &&
                    requestedMsaa != 4 && requestedMsaa != 8)
                {
                    requestedMsaa = 1;
                }

                renderTarget = new RenderTexture(
                    CaptureWidth,
                    CaptureHeight,
                    24,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB)
                {
                    name = "DungeonPortalTransportSmokeCapture_Render",
                    hideFlags = HideFlags.HideAndDontSave,
                    antiAliasing = requestedMsaa,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                readbackTarget = new RenderTexture(
                    CaptureWidth,
                    CaptureHeight,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB)
                {
                    name = "DungeonPortalTransportSmokeCapture_Readback",
                    hideFlags = HideFlags.HideAndDontSave,
                    antiAliasing = 1,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                if (!renderTarget.Create() || !readbackTarget.Create())
                    throw new InvalidOperationException("Unable to create tool-owned SMOKE render targets.");
            }

            private void AppendState(StateEvidence state)
            {
                var list = new List<StateEvidence>(manifest.states ?? Array.Empty<StateEvidence>())
                {
                    state
                };
                manifest.states = list.ToArray();
                manifest.capturedStateCount = manifest.states.Length;
                manifest.capturedImageCount += state.images != null ? state.images.Length : 0;
            }

            private void Finish(Exception failure)
            {
                if (finished)
                    return;
                finished = true;
                Unsubscribe();

                var restorationFailures = new List<string>();
                RestorePower(startPower, startPowerSnapshot, "Start", restorationFailures);
                RestorePower(adminPower, adminPowerSnapshot, "Admin", restorationFailures);
                RestoreDoor(restorationFailures);
                RestoreCameras(restorationFailures);
                ReleaseRenderTargets();

                manifest.completedUtcIso8601 = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                manifest.outcome = failure == null && restorationFailures.Count == 0 &&
                                   manifest.capturedStateCount == ExpectedStateCount &&
                                   manifest.capturedImageCount == ExpectedImageCount
                    ? "COMPLETE"
                    : "ABORTED";
                manifest.failure = failure != null ? failure.ToString() : string.Empty;
                manifest.restoration = new RestorationEvidence
                {
                    startPowerCurrentRestoredAtFinalize = Approximately(
                        startPower != null ? startPower.Power01 : float.NaN,
                        startPowerSnapshot.Current),
                    startPowerTargetRestoredAtFinalize = Approximately(
                        startPower != null ? startPower.TargetPower01 : float.NaN,
                        startPowerSnapshot.Target),
                    adminPowerCurrentRestoredAtFinalize = Approximately(
                        adminPower != null ? adminPower.Power01 : float.NaN,
                        adminPowerSnapshot.Current),
                    adminPowerTargetRestoredAtFinalize = Approximately(
                        adminPower != null ? adminPower.TargetPower01 : float.NaN,
                        adminPowerSnapshot.Target),
                    doorLocalRotationRestoredAtFinalize = doorLeaf != null &&
                        Quaternion.Angle(doorLeaf.localRotation, originalDoorLocalRotation) <= 0.001f,
                    cameraStateRestoredAtFinalize = AllCamerasRestored(),
                    failures = restorationFailures.ToArray(),
                    limitation =
                        "Preflight rejects in-progress power transitions. Stable power current/target values are restored through the public API."
                };

                Exception evidenceWriteFailure = null;
                try
                {
                    CreateOutputDirectory();
                    for (int i = 0; i < pendingFiles.Count; i++)
                    {
                        PendingFile file = pendingFiles[i];
                        WriteNewFile(Path.Combine(outputAbsoluteFolder, file.Filename), file.Bytes);
                    }
                }
                catch (Exception exception)
                {
                    evidenceWriteFailure = exception;
                    manifest.outcome = "ABORTED";
                    manifest.failure = CombineFailures(manifest.failure, "Evidence write failed: " + exception);
                }

                string manifestPath = Path.Combine(outputAbsoluteFolder, "manifest.json");
                Exception manifestFailure = null;
                try
                {
                    if (Directory.Exists(outputAbsoluteFolder))
                    {
                        string json = JsonUtility.ToJson(manifest, true);
                        WriteNewFile(manifestPath, new System.Text.UTF8Encoding(false).GetBytes(json));
                    }
                    else
                    {
                        throw new IOException("The isolated SMOKE output folder was not created.");
                    }
                }
                catch (Exception exception)
                {
                    manifestFailure = exception;
                }

                finishedCallback?.Invoke(this);

                if (failure == null && restorationFailures.Count == 0 &&
                    evidenceWriteFailure == null && manifestFailure == null &&
                    string.Equals(manifest.outcome, "COMPLETE", StringComparison.Ordinal))
                {
                    Debug.Log(
                        "COMPLETE portal-transport SMOKE capture (visual acceptance remains unclaimed). status=" +
                        manifest.status + " images=" + manifest.capturedImageCount +
                        " output=" + OutputAssetFolder);
                }
                else
                {
                    Debug.LogError(
                        "ABORTED portal-transport SMOKE capture. output=" + OutputAssetFolder +
                        " failure=" + (failure != null ? failure.Message : "none") +
                        " restorationFailures=" + string.Join(" | ", restorationFailures) +
                        " evidenceWriteFailure=" +
                        (evidenceWriteFailure != null ? evidenceWriteFailure.Message : "none") +
                        " manifestFailure=" + (manifestFailure != null ? manifestFailure.Message : "none"));
                }
            }

            private void CreateOutputDirectory()
            {
                if (Directory.Exists(outputAbsoluteFolder))
                    throw new InvalidOperationException(
                        "Refusing to reuse existing SMOKE output '" + OutputAssetFolder + "'.");
                Directory.CreateDirectory(outputAbsoluteFolder);
            }

            private void HandlePlayModeStateChanged(PlayModeStateChange change)
            {
                if (finished)
                    return;
                if (change == PlayModeStateChange.ExitingPlayMode ||
                    change == PlayModeStateChange.EnteredEditMode)
                {
                    Finish(new InvalidOperationException(
                        "Play Mode changed while capture was running; the tool did not request that change."));
                }
            }

            private void HandleBeforeAssemblyReload()
            {
                if (!finished)
                {
                    Finish(new InvalidOperationException(
                        "Assembly reload began while capture was running."));
                }
            }

            private void Unsubscribe()
            {
                if (!subscribed)
                    return;
                EditorApplication.update -= Update;
                EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
                AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload;
                subscribed = false;
            }

            private static void RestorePower(
                DungeonPortalPowerEnvelope envelope,
                PowerSnapshot snapshot,
                string label,
                List<string> failures)
            {
                if (envelope == null)
                {
                    failures.Add(label + " power envelope was destroyed before restoration.");
                    return;
                }

                try
                {
                    envelope.SetImmediate(snapshot.Current);
                    if (!Approximately(snapshot.Current, snapshot.Target))
                        envelope.SetTargetPower(snapshot.Target);
                    if (!Approximately(envelope.Power01, snapshot.Current) ||
                        !Approximately(envelope.TargetPower01, snapshot.Target))
                    {
                        failures.Add(label + " power current/target did not restore exactly at finalize.");
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(label + " power restoration threw: " + exception.Message);
                }
            }

            private void RestoreDoor(List<string> failures)
            {
                if (doorLeaf == null || doorSource == null)
                {
                    failures.Add("Door leaf/source was destroyed before restoration.");
                    return;
                }

                try
                {
                    doorLeaf.localRotation = originalDoorLocalRotation;
                    doorSource.EvaluateNow();
                    if (Quaternion.Angle(doorLeaf.localRotation, originalDoorLocalRotation) > 0.001f)
                        failures.Add("Door leaf local rotation did not restore.");
                }
                catch (Exception exception)
                {
                    failures.Add("Door restoration threw: " + exception.Message);
                }
            }

            private void RestoreCameras(List<string> failures)
            {
                for (int i = 0; i < cameraSnapshots.Length; i++)
                {
                    try
                    {
                        cameraSnapshots[i].Restore();
                        if (!cameraSnapshots[i].IsRestored())
                            failures.Add(cameraSnapshots[i].CameraId + " camera state did not restore.");
                    }
                    catch (Exception exception)
                    {
                        failures.Add(cameraSnapshots[i].CameraId +
                                     " camera restoration threw: " + exception.Message);
                    }
                }
            }

            private bool AllCamerasRestored()
            {
                for (int i = 0; i < cameraSnapshots.Length; i++)
                {
                    if (!cameraSnapshots[i].IsRestored())
                        return false;
                }
                return true;
            }

            private void ReleaseRenderTargets()
            {
                if (renderTarget != null)
                {
                    renderTarget.Release();
                    UnityEngine.Object.DestroyImmediate(renderTarget);
                    renderTarget = null;
                }
                if (readbackTarget != null)
                {
                    readbackTarget.Release();
                    UnityEngine.Object.DestroyImmediate(readbackTarget);
                    readbackTarget = null;
                }
            }

            private static StateDefinition[] BuildStates()
            {
                float[,] powers =
                {
                    { 0f, 0f },
                    { 1f, 0f },
                    { 0f, 1f },
                    { 1f, 1f }
                };
                int[] doors = { 0, 45, 90 };
                var result = new StateDefinition[ExpectedStateCount];
                int index = 0;
                for (int powerIndex = 0; powerIndex < powers.GetLength(0); powerIndex++)
                {
                    for (int doorIndex = 0; doorIndex < doors.Length; doorIndex++)
                    {
                        int startPercent = Mathf.RoundToInt(powers[powerIndex, 0] * 100f);
                        int adminPercent = Mathf.RoundToInt(powers[powerIndex, 1] * 100f);
                        int doorDegrees = doors[doorIndex];
                        result[index++] = new StateDefinition
                        {
                            stateId = "P" + startPercent.ToString("000", CultureInfo.InvariantCulture) +
                                      "_P" + adminPercent.ToString("000", CultureInfo.InvariantCulture) +
                                      "_D" + doorDegrees.ToString("000", CultureInfo.InvariantCulture),
                            startPower01 = powers[powerIndex, 0],
                            adminPower01 = powers[powerIndex, 1],
                            doorDegrees = doorDegrees,
                            doorOpenFraction = doorDegrees / 90f
                        };
                    }
                }
                return result;
            }

            private readonly struct PendingFile
            {
                public PendingFile(string filename, byte[] bytes)
                {
                    Filename = filename;
                    Bytes = bytes;
                }

                public string Filename { get; }
                public byte[] Bytes { get; }
            }
        }

        private static EndpointEvidence BuildEndpointEvidence(
            string label,
            DungeonPortalEndpoint endpoint)
        {
            var result = new EndpointEvidence
            {
                endpointLabel = label,
                endpointObjectName = endpoint != null ? endpoint.gameObject.name : string.Empty,
                endpointConfigured = endpoint != null && endpoint.IsConfigured,
                endpointConnectionActive = endpoint != null && endpoint.IsConnectionActive
            };
            if (endpoint == null)
            {
                result.profileValidationReason = "Endpoint is missing.";
                return result;
            }

            DungeonPortalEndpointProfile profile = endpoint.Profile;
            result.profilePresent = profile != null;
            if (profile == null)
            {
                result.profileValidationReason = "Endpoint profile is null.";
                return result;
            }

            result.roomId = profile.RoomId;
            result.doorwayId = profile.DoorwayId;
            result.profileAssetPath = AssetDatabase.GetAssetPath(profile);
            result.profileGuid = AssetDatabase.AssetPathToGUID(result.profileAssetPath);
            result.profileDependencyHash = GetDependencyHashOrEmpty(result.profileAssetPath);
            result.incomingBounceCount = profile.IncomingBounceLights.Length;
            result.profileValid = profile.TryValidate(out string profileFailure);
            result.profileValidationReason = profileFailure ?? string.Empty;

            string fitPath = "Assets/Experiments/DungeonPortalTransportPoC/FitEvidence/" +
                             result.roomId + "_K1ReceiverBounceFitEvidence.asset";
            DungeonPortalReceiverBounceFitEvidence fit =
                AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverBounceFitEvidence>(fitPath);
            result.fitEvidencePath = fitPath;
            result.fitEvidenceGuid = AssetDatabase.AssetPathToGUID(fitPath);
            result.fitEvidenceDependencyHash = GetDependencyHashOrEmpty(fitPath);
            result.fitEvidencePresent = fit != null;
            if (fit != null)
            {
                result.fitEvidenceStatus = fit.Status;
                result.fitEvidenceHash = fit.EvidenceHash;
                result.fitEvidenceAccepted = fit.IsAccepted;
                result.fitEvidenceValid = fit.TryValidate(out string fitFailure) &&
                                          string.Equals(fit.ReceiverRoomId, result.roomId, StringComparison.Ordinal) &&
                                          fit.TargetProfile == profile;
                result.fitEvidenceValidationReason = fitFailure ?? string.Empty;
                if (result.fitEvidenceValid == false && string.IsNullOrWhiteSpace(result.fitEvidenceValidationReason))
                    result.fitEvidenceValidationReason = "Fit evidence identity/profile binding does not match the endpoint.";
            }
            else
            {
                result.fitEvidenceValidationReason = "Fit evidence asset is missing.";
            }

            string artifactPath = "Assets/Experiments/DungeonPortalTransportPoC/Generated/RenderArtifacts/" +
                                  result.roomId + "/Current/" + result.roomId +
                                  "_DirectOnlyRenderArtifact.asset";
            DungeonPortalReceiverBounceRenderArtifact artifact =
                AssetDatabase.LoadAssetAtPath<DungeonPortalReceiverBounceRenderArtifact>(artifactPath);
            result.renderArtifactPath = artifactPath;
            result.renderArtifactGuid = AssetDatabase.AssetPathToGUID(artifactPath);
            result.renderArtifactDependencyHash = GetDependencyHashOrEmpty(artifactPath);
            result.renderArtifactPresent = artifact != null;
            if (artifact != null)
            {
                result.renderArtifactStatus = artifact.Status;
                result.renderArtifactHash = artifact.ArtifactHash;
                result.renderArtifactReady = artifact.IsRenderBaselineReady;
                result.renderArtifactPersistedValid =
                    DungeonPortalReceiverBounceRenderArtifactAuthoring.TryValidatePersistedArtifact(
                        result.roomId,
                        out string artifactFailure) &&
                    string.Equals(artifact.ReceiverRoomId, result.roomId, StringComparison.Ordinal);
                result.renderArtifactValidationReason = artifactFailure ?? string.Empty;
                if (!result.renderArtifactPersistedValid &&
                    string.IsNullOrWhiteSpace(result.renderArtifactValidationReason))
                {
                    result.renderArtifactValidationReason = "Render artifact receiver identity does not match the endpoint.";
                }
            }
            else
            {
                result.renderArtifactValidationReason = "Persisted Stage A render artifact is missing.";
            }

            result.acceptedReceiverEvidence = result.profileValid &&
                                              result.incomingBounceCount > 0 &&
                                              result.fitEvidencePresent &&
                                              result.fitEvidenceAccepted &&
                                              result.fitEvidenceValid &&
                                              result.renderArtifactPresent &&
                                              result.renderArtifactReady &&
                                              result.renderArtifactPersistedValid;
            return result;
        }

        private static void AppendEndpointReadiness(
            EndpointEvidence endpoint,
            List<string> reasons)
        {
            if (!endpoint.profilePresent)
                reasons.Add(endpoint.endpointLabel + " endpoint profile is missing.");
            else if (!endpoint.profileValid)
                reasons.Add(endpoint.endpointLabel + " endpoint profile is invalid: " + endpoint.profileValidationReason);
            if (endpoint.incomingBounceCount <= 0)
                reasons.Add(endpoint.endpointLabel + " endpoint has no accepted incoming bounce descriptor.");
            if (!endpoint.fitEvidenceAccepted || !endpoint.fitEvidenceValid)
            {
                reasons.Add(endpoint.endpointLabel + " K=1 fit evidence is not accepted and valid: status='" +
                            endpoint.fitEvidenceStatus + "' reason='" + endpoint.fitEvidenceValidationReason + "'.");
            }
            if (!endpoint.renderArtifactReady || !endpoint.renderArtifactPersistedValid)
            {
                reasons.Add(endpoint.endpointLabel + " Stage A render artifact is not READY and persisted-valid: status='" +
                            endpoint.renderArtifactStatus + "' reason='" + endpoint.renderArtifactValidationReason + "'.");
            }
        }

        private sealed class CameraSnapshot
        {
            private readonly RenderTexture targetTexture;
            private readonly float aspect;
            private readonly Matrix4x4 projectionMatrix;
            private readonly Matrix4x4 nonJitteredProjectionMatrix;
            private readonly bool useJitteredProjectionForTransparent;
            public CameraSnapshot(string cameraId, Camera camera)
            {
                CameraId = cameraId;
                Camera = camera;
                targetTexture = camera.targetTexture;
                aspect = camera.aspect;
                projectionMatrix = camera.projectionMatrix;
                nonJitteredProjectionMatrix = camera.nonJitteredProjectionMatrix;
                useJitteredProjectionForTransparent =
                    camera.useJitteredProjectionMatrixForTransparentRendering;
            }

            public string CameraId { get; }
            public Camera Camera { get; }

            public void Restore()
            {
                if (Camera == null)
                    return;
                Camera.targetTexture = targetTexture;
                Camera.aspect = aspect;
                Camera.projectionMatrix = projectionMatrix;
                Camera.nonJitteredProjectionMatrix = nonJitteredProjectionMatrix;
                Camera.useJitteredProjectionMatrixForTransparentRendering =
                    useJitteredProjectionForTransparent;
                // Preflight requires both fixed cameras to be disabled.  Keep that
                // invariant explicit rather than carrying a value that could enable one.
                Camera.enabled = false;
            }

            public bool IsRestored()
            {
                return Camera != null && Camera.targetTexture == targetTexture &&
                       Mathf.Abs(Camera.aspect - aspect) <= 0.000001f &&
                       MatrixApproximately(Camera.projectionMatrix, projectionMatrix) &&
                       MatrixApproximately(Camera.nonJitteredProjectionMatrix, nonJitteredProjectionMatrix) &&
                       Camera.useJitteredProjectionMatrixForTransparentRendering ==
                       useJitteredProjectionForTransparent &&
                       !Camera.enabled;
            }
        }

        private readonly struct PowerSnapshot
        {
            public PowerSnapshot(DungeonPortalPowerEnvelope envelope)
            {
                Current = envelope.Power01;
                Target = envelope.TargetPower01;
            }

            public float Current { get; }
            public float Target { get; }
        }

        private struct StateDefinition
        {
            public string stateId;
            public float startPower01;
            public float adminPower01;
            public int doorDegrees;
            public float doorOpenFraction;
        }

        [Serializable]
        private sealed class SmokeManifest
        {
            public int schemaVersion;
            public string toolVersion;
            public string status;
            public string outcome;
            public string failure;
            public string startedUtcIso8601;
            public string completedUtcIso8601;
            public string unityVersion;
            public string sceneAssetPath;
            public string sceneGuid;
            public string sceneDependencyHash;
            public string outputAssetFolder;
            public int width;
            public int height;
            public int minimumRuntimeSettleFrames;
            public int expectedStateCount;
            public int expectedImageCount;
            public int capturedStateCount;
            public int capturedImageCount;
            public bool playModeOwnedByTool;
            public bool sceneOpenedOrSavedByTool;
            public bool connectionMutatedByTool;
            public bool camerasEnabledByTool;
            public bool userCameraOrSelectionMutatedByTool;
            public bool realtimeReferenceCaptured;
            public bool visualParityClaimed;
            public bool connectionComponentActiveAtStart;
            public bool bothEndpointsConnectionActiveAtStart;
            public EndpointEvidence startEndpoint;
            public EndpointEvidence adminEndpoint;
            public string[] readinessReasons;
            public StateEvidence[] states;
            public RestorationEvidence restoration;
        }

        [Serializable]
        private sealed class EndpointEvidence
        {
            public string endpointLabel;
            public string endpointObjectName;
            public bool endpointConfigured;
            public bool endpointConnectionActive;
            public bool profilePresent;
            public bool profileValid;
            public string profileValidationReason;
            public string roomId;
            public string doorwayId;
            public string profileAssetPath;
            public string profileGuid;
            public string profileDependencyHash;
            public int incomingBounceCount;
            public bool fitEvidencePresent;
            public bool fitEvidenceAccepted;
            public bool fitEvidenceValid;
            public string fitEvidenceStatus;
            public string fitEvidenceHash;
            public string fitEvidencePath;
            public string fitEvidenceGuid;
            public string fitEvidenceDependencyHash;
            public string fitEvidenceValidationReason;
            public bool renderArtifactPresent;
            public bool renderArtifactReady;
            public bool renderArtifactPersistedValid;
            public string renderArtifactStatus;
            public string renderArtifactHash;
            public string renderArtifactPath;
            public string renderArtifactGuid;
            public string renderArtifactDependencyHash;
            public string renderArtifactValidationReason;
            public bool acceptedReceiverEvidence;
        }

        [Serializable]
        private sealed class StateEvidence
        {
            public string stateId;
            public float requestedStartPower01;
            public float requestedAdminPower01;
            public int requestedDoorDegrees;
            public float requestedDoorOpenFraction;
            public float actualStartPower01;
            public float actualStartTargetPower01;
            public float actualAdminPower01;
            public float actualAdminTargetPower01;
            public float actualDoorOpenFraction;
            public float actualApertureFraction;
            public FloatVector actualDoorLocalRotation;
            public int appliedRuntimeFrame;
            public int capturedRuntimeFrame;
            public int settledRuntimeFrames;
            public bool connectionComponentActive;
            public bool startEndpointConnectionActive;
            public bool adminEndpointConnectionActive;
            public ImageEvidence[] images;
        }

        [Serializable]
        private sealed class ImageEvidence
        {
            public string cameraId;
            public string cameraName;
            public string assetPath;
            public string sha256;
            public long byteCount;
            public bool cameraRemainedDisabled;
            public FloatVector cameraPosition;
            public FloatVector cameraRotation;
            public FloatMatrix cameraLocalToWorldMatrix;
            public FloatMatrix worldToCameraMatrix;
            public FloatMatrix projectionMatrix;
            public FloatMatrix gpuProjectionMatrix;
            public float aspect;
            public float fieldOfView;
            public bool orthographic;
            public float orthographicSize;
            public float nearClipPlane;
            public float farClipPlane;
            public int cullingMask;
            public bool allowHdr;
            public bool allowMsaa;
            public string renderTargetFormat;
            public int renderTargetAntiAliasing;
        }

        [Serializable]
        private sealed class RestorationEvidence
        {
            public bool startPowerCurrentRestoredAtFinalize;
            public bool startPowerTargetRestoredAtFinalize;
            public bool adminPowerCurrentRestoredAtFinalize;
            public bool adminPowerTargetRestoredAtFinalize;
            public bool doorLocalRotationRestoredAtFinalize;
            public bool cameraStateRestoredAtFinalize;
            public string[] failures = Array.Empty<string>();
            public string limitation;
        }

        [Serializable]
        private struct FloatVector
        {
            public float x;
            public float y;
            public float z;
            public float w;

            public static FloatVector FromVector3(Vector3 value)
            {
                return new FloatVector { x = value.x, y = value.y, z = value.z, w = 0f };
            }

            public static FloatVector FromQuaternion(Quaternion value)
            {
                return new FloatVector { x = value.x, y = value.y, z = value.z, w = value.w };
            }
        }

        [Serializable]
        private struct FloatMatrix
        {
            public string order;
            public float[] values;

            public static FloatMatrix From(Matrix4x4 value)
            {
                var values = new float[16];
                int index = 0;
                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 4; column++)
                        values[index++] = value[row, column];
                }

                return new FloatMatrix { order = "row-major", values = values };
            }
        }

        private static string AssetPathToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new InvalidOperationException("Unable to resolve the Unity project root.");
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static string GetDependencyHashOrEmpty(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) ||
                string.IsNullOrWhiteSpace(AssetDatabase.AssetPathToGUID(assetPath)))
            {
                return string.Empty;
            }
            return AssetDatabase.GetAssetDependencyHash(assetPath).ToString();
        }

        private static void WriteNewFile(string absolutePath, byte[] bytes)
        {
            using (var stream = new FileStream(
                       absolutePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var builder = new System.Text.StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static string CombineFailures(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first))
                return second ?? string.Empty;
            if (string.IsNullOrWhiteSpace(second))
                return first;
            return first + Environment.NewLine + second;
        }

        private static void AssertApproximately(
            float actual,
            float expected,
            string label,
            float tolerance = 0.0001f)
        {
            if (float.IsNaN(actual) || float.IsInfinity(actual) ||
                Mathf.Abs(actual - expected) > tolerance)
            {
                throw new InvalidOperationException(
                    label + " did not settle. expected=" + expected.ToString("R", CultureInfo.InvariantCulture) +
                    " actual=" + actual.ToString("R", CultureInfo.InvariantCulture) + ".");
            }
        }

        private static bool Approximately(float first, float second)
        {
            return !float.IsNaN(first) && !float.IsInfinity(first) &&
                   !float.IsNaN(second) && !float.IsInfinity(second) &&
                   Mathf.Abs(first - second) <= 0.0001f;
        }

        private static bool MatrixApproximately(Matrix4x4 first, Matrix4x4 second)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (Mathf.Abs(first[row, column] - second[row, column]) > 0.000001f)
                        return false;
                }
            }
            return true;
        }
    }
}
