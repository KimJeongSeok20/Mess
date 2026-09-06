using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Demo.Scripts.Runtime.Character;
using DunGen;
using DunGen.Graph;
using DungeonPortalTransportPoC;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DungeonPortalBakedBasisPoC.Validation
{
    public enum DungeonPortalBakedBasisEvidenceSpotPolicy
    {
        ORACLE_SPOT_OFF = 0,
        HUMAN_SPOT_ON = 1
    }

    /// <summary>
    /// Runtime bootstrap for the DPBB pair in the real StartMap dungeon-entry environment.
    /// The DPBB scene is the bootstrap scene so its baked lightmap table remains intact; StartMap
    /// is loaded additively and made active before generation. No synthetic camera or fallback
    /// Volume is permitted.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    [DisallowMultipleComponent]
    public sealed class DungeonPortalBakedBasisStartMapRuntimeController : MonoBehaviour
    {
        public const string StartMapScenePath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
        public const string RequiredDungeonVolumeProfileName = "37_Drone View";
        public const string ValidationOnlyGlobalVolumeName =
            "04_ValidationGlobalVolume_StartMapParity";
        public const string RequiredDungeonFlowAssetPath =
            "Assets/Prefabs/map_piece/NewPrison/New_Prison_V2_Flow.asset";
        public const string RequiredStartTileSetAssetPath =
            "Assets/Prefabs/map_piece/NewPrison/New_Prison_V2_StartTiles.asset";
        public const string RequiredAdministrativeTileSetAssetPath =
            "Assets/Prefabs/map_piece/NewPrison/New_Prison_V2_Rooms.asset";
        public const string RequiredStartPrefabAssetPath =
            "Assets/Prefabs/map_piece/NewPrison/test/V2_StartRoom/StartRoom.prefab";
        public const string RequiredAdministrativePrefabAssetPath =
            "Assets/Prefabs/map_piece/NewPrison/test/V2_AdminstrativeSegregation/" +
            "AdminstrativeSegregation.prefab";

        private const float DoorwayPositionTolerance = 0.001f;
        private const float DoorwayRotationToleranceDegrees = 0.05f;
        private const float TileContainmentTolerance = 0.02f;
        private const int MaximumGenerationAttempts = 16;

        [Header("DPBB validation pair")]
        [SerializeField] private GameObject validationPairRoot;
        [SerializeField] private Camera startToAdministrativeReferenceCamera;
        [SerializeField] private Camera administrativeToStartReferenceCamera;
        [SerializeField] private DungeonPortalBakedBasisConnectionDriver connectionDriver;
        [SerializeField] private DungeonPortalBakedBasisDoorShDriver doorShDriver;
        [SerializeField] private DungeonPortalBakedBasisReflectionDriver reflectionDriver;
        [SerializeField] private Transform validationStartRoom;
        [SerializeField] private Transform validationAdministrativeRoom;
        [SerializeField] private Transform validationStartDoorway;
        [SerializeField] private Transform validationAdministrativeDoorway;

        [Header("Actual StartMap bootstrap")]
        [SerializeField, Min(10f)] private float startupTimeoutSeconds = 60f;
        [SerializeField] private DungeonFlow requiredDungeonFlow;
        [SerializeField] private TileSet requiredStartTileSet;
        [SerializeField] private TileSet requiredAdministrativeTileSet;
        [SerializeField] private GameObject requiredStartPrefab;
        [SerializeField] private GameObject requiredAdministrativePrefab;
        [SerializeField] private DungeonPortalBakedRoomBasisData requiredStartRoomBasis;
        [SerializeField] private DungeonPortalBakedRoomBasisData requiredAdministrativeRoomBasis;
        [SerializeField] private Shader requiredCompositionShader;
        [SerializeField] private DungeonPortalBakedBasisEvidenceSpotPolicy evidenceSpotPolicy =
            DungeonPortalBakedBasisEvidenceSpotPolicy.HUMAN_SPOT_ON;

        private string status = "WAITING";
        private string failureReason;
        private string generationResult = "NONE";
        private string dungeonReadyResult = "NONE";
        private string entryResult = "NONE";
        private Scene startMapScene;
        private DungeonZoneManager zoneManager;
        private FPSController localPlayerController;
        private Transform localPlayerRoot;
        private Camera actualPlayerCamera;
        private Volume assignedDungeonVolume;
        private Light assignedDungeonOnlyLight;
        private Dungeon generatedDungeon;
        private Tile generatedStartTile;
        private Tile generatedAdministrativeTile;
        private Doorway generatedStartDoorway;
        private Doorway generatedAdministrativeDoorway;
        private Transform actualGeneratedDoorLeaf;
        private int generatedRoomCount;
        private int generationAttemptCount;
        private readonly List<string> generationAttemptSummaries = new List<string>();
        private int currentFrameIndex;
        private DungeonPortalBakedBasisRuntimeEnvironmentFingerprint environmentFingerprint;
        private DungeonPortalBakedBasisRuntimeEnvironmentFingerprint oracleSpotOffFingerprint;
        private DungeonPortalBakedBasisRuntimeEnvironmentFingerprint humanSpotOnFingerprint;
        private bool dungeonOnlyLightSnapshotCaptured;
        private bool originalDungeonOnlyLightEnabled;
        private bool connectionAdjacentSnapshotCaptured;
        private bool originalConnectionAdjacentEnabled;
        private readonly List<RendererSnapshot> generatedRendererSnapshots =
            new List<RendererSnapshot>();
        private readonly HashSet<Renderer> generatedBasisBoundRenderers =
            new HashSet<Renderer>();
        private readonly HashSet<Renderer> generatedDoorShRenderers =
            new HashSet<Renderer>();
        private readonly List<ReflectionProbe> generatedProductionProbes =
            new List<ReflectionProbe>();
        private readonly List<RendererSnapshot> validationRendererSnapshots =
            new List<RendererSnapshot>();
        private readonly List<ProbeSnapshot> validationProbeSnapshots =
            new List<ProbeSnapshot>();
        private readonly List<LightSnapshot> validationLightSnapshots =
            new List<LightSnapshot>();
        private readonly List<ColliderSnapshot> validationColliderSnapshots =
            new List<ColliderSnapshot>();
        private readonly List<SwitcherSnapshot> validationSwitcherSnapshots =
            new List<SwitcherSnapshot>();
        private readonly List<BehaviourSnapshot> productionDoorReceiverSnapshots =
            new List<BehaviourSnapshot>();
        private readonly List<Bounds> generatedNonTargetTileBounds = new List<Bounds>();

        public string Status => status;
        public string FailureReason => failureReason;
        public string GenerationResult => generationResult;
        public string DungeonReadyResult => dungeonReadyResult;
        public string EntryResult => entryResult;
        public bool IsReady => string.Equals(status, "READY", StringComparison.Ordinal);
        public Scene StartMapScene => startMapScene;
        public int GeneratedRoomCount => generatedRoomCount;
        public int GenerationAttemptCount => generationAttemptCount;
        public IReadOnlyList<string> GenerationAttemptSummaries => generationAttemptSummaries;
        public int CurrentFrameIndex => currentFrameIndex;
        public DungeonPortalBakedBasisEvidenceSpotPolicy EvidenceSpotPolicy => evidenceSpotPolicy;
        public Camera ActualPlayerCamera => actualPlayerCamera;
        public GameObject ValidationPairRoot => validationPairRoot;
        public DungeonPortalBakedBasisConnectionDriver ConnectionDriver => connectionDriver;
        public DungeonPortalBakedBasisDoorShDriver DoorShDriver => doorShDriver;
        public DungeonPortalBakedBasisReflectionDriver ReflectionDriver => reflectionDriver;
        public Transform ActualGeneratedDoorLeaf => actualGeneratedDoorLeaf;
        public DungeonFlow RequiredDungeonFlow => requiredDungeonFlow;
        public TileSet RequiredStartTileSet => requiredStartTileSet;
        public TileSet RequiredAdministrativeTileSet => requiredAdministrativeTileSet;
        public GameObject RequiredStartPrefab => requiredStartPrefab;
        public GameObject RequiredAdministrativePrefab => requiredAdministrativePrefab;
        public DungeonPortalBakedRoomBasisData RequiredStartRoomBasis => requiredStartRoomBasis;
        public DungeonPortalBakedRoomBasisData RequiredAdministrativeRoomBasis =>
            requiredAdministrativeRoomBasis;
        public Shader RequiredCompositionShader => requiredCompositionShader;
        public DungeonPortalBakedBasisRuntimeEnvironmentFingerprint EnvironmentFingerprint =>
            ResolveEnvironmentFingerprint(evidenceSpotPolicy);
        public DungeonPortalBakedBasisRuntimeEnvironmentFingerprint OracleSpotOffEnvironmentFingerprint =>
            oracleSpotOffFingerprint;
        public DungeonPortalBakedBasisRuntimeEnvironmentFingerprint HumanSpotOnEnvironmentFingerprint =>
            humanSpotOnFingerprint;
        public Volume AssignedDungeonVolume => assignedDungeonVolume;
        public bool IsActualDungeonEnvironmentApplied =>
            startMapScene.IsValid() && startMapScene.isLoaded &&
            SceneManager.GetActiveScene() == startMapScene && zoneManager != null && zoneManager.IsInDungeon;

        public void ConfigureAuthoring(
            GameObject pairRoot,
            Camera startToAdministrativeFrame,
            Camera administrativeToStartFrame,
            DungeonPortalBakedBasisConnectionDriver driver,
            DungeonPortalBakedBasisDoorShDriver doorSh,
            DungeonPortalBakedBasisReflectionDriver reflections,
            Transform startRoom,
            Transform administrativeRoom,
            Transform startDoorway,
            Transform administrativeDoorway,
            DungeonFlow dungeonFlow,
            TileSet startTileSet,
            TileSet administrativeTileSet,
            GameObject startPrefab,
            GameObject administrativePrefab,
            DungeonPortalBakedRoomBasisData startRoomBasis = null,
            DungeonPortalBakedRoomBasisData administrativeRoomBasis = null,
            Shader compositionShader = null)
        {
            validationPairRoot = pairRoot;
            startToAdministrativeReferenceCamera = startToAdministrativeFrame;
            administrativeToStartReferenceCamera = administrativeToStartFrame;
            connectionDriver = driver;
            doorShDriver = doorSh;
            reflectionDriver = reflections;
            validationStartRoom = startRoom;
            validationAdministrativeRoom = administrativeRoom;
            validationStartDoorway = startDoorway;
            validationAdministrativeDoorway = administrativeDoorway;
            requiredDungeonFlow = dungeonFlow;
            requiredStartTileSet = startTileSet;
            requiredAdministrativeTileSet = administrativeTileSet;
            requiredStartPrefab = startPrefab;
            requiredAdministrativePrefab = administrativePrefab;
            requiredStartRoomBasis = startRoomBasis;
            requiredAdministrativeRoomBasis = administrativeRoomBasis;
            requiredCompositionShader = compositionShader;
        }

        private void Awake()
        {
            // The pair cannot start its compositors before StartMap's player, render-layer and
            // dungeon Volume contract exist. This also prevents accidental use as a fallback scene.
            if (validationPairRoot != null)
                validationPairRoot.SetActive(false);
        }

        private IEnumerator Start()
        {
            yield return Bootstrap();
        }

        private IEnumerator Bootstrap()
        {
            if (!TryValidateAuthoring(out string authoringFailure))
            {
                Fail(authoringFailure);
                yield break;
            }

            status = "LOADING ACTUAL STARTMAP";
            yield return LoadActualStartMap();
            if (!string.IsNullOrEmpty(failureReason))
                yield break;

            float deadline = Time.realtimeSinceStartup + startupTimeoutSeconds;
            status = "WAITING FOR ACTUAL STARTMAP PLAYER/SERVER/ZONE";
            string zoneResolutionFailure = null;
            NetworkDungeonController networkDungeon = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                localPlayerController = FindLocalPlayerController();
                localPlayerRoot = localPlayerController != null ? localPlayerController.transform.root : null;
                bool hasUniqueProductionZone = TryResolveUniqueProductionZoneManager(
                    out zoneManager,
                    out zoneResolutionFailure);
                networkDungeon = FindAnyObjectByType<NetworkDungeonController>();
                if (localPlayerRoot != null && hasUniqueProductionZone && networkDungeon != null &&
                    networkDungeon.isServer)
                {
                    break;
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }

            if (localPlayerRoot == null || zoneManager == null || networkDungeon == null ||
                !networkDungeon.isServer)
            {
                Fail("Actual StartMap local owner, server, or unique production DungeonZoneManager " +
                     "did not become ready. Zone=" + zoneResolutionFailure);
                yield break;
            }
            if (!TryValidateProductionMapList(networkDungeon, out string mapListFailure))
            {
                Fail(mapListFailure);
                yield break;
            }

            RuntimeDungeon runtimeDungeon = null;
            Transform pairTransform = validationPairRoot.transform;
            Vector3 originalPairPosition = pairTransform.position;
            Quaternion originalPairRotation = pairTransform.rotation;
            generationAttemptSummaries.Clear();
            generationAttemptCount = 0;
            while (generationAttemptCount < MaximumGenerationAttempts &&
                   Time.realtimeSinceStartup < deadline)
            {
                generationAttemptCount++;
                validationPairRoot.SetActive(false);
                RestoreGeneratedPairState();
                pairTransform.SetPositionAndRotation(originalPairPosition, originalPairRotation);
                generatedDungeon = null;
                generatedStartTile = null;
                generatedAdministrativeTile = null;
                generatedStartDoorway = null;
                generatedAdministrativeDoorway = null;
                generatedRoomCount = 0;
                runtimeDungeon = FindAnyObjectByType<RuntimeDungeon>();
                status = "GENERATING REQUIRED START/ADMIN PAIR " + generationAttemptCount + "/" +
                         MaximumGenerationAttempts;
                generationResult = DebugRemoteControl.GenerateDungeon();
                if (IsError(generationResult))
                {
                    Fail("DebugRemoteControl.GenerateDungeon failed at attempt " +
                         generationAttemptCount + ": " + generationResult);
                    yield break;
                }

                Dungeon current = null;
                while (Time.realtimeSinceStartup < deadline)
                {
                    runtimeDungeon = FindAnyObjectByType<RuntimeDungeon>();
                    current = runtimeDungeon != null && runtimeDungeon.Generator != null
                        ? runtimeDungeon.Generator.CurrentDungeon
                        : null;
                    if (current != null && current.AllTiles != null && current.AllTiles.Count > 0 &&
                        !runtimeDungeon.Generator.IsGenerating && networkDungeon.CurrentSeed != 0 &&
                        runtimeDungeon.Generator.Seed == networkDungeon.CurrentSeed)
                    {
                        break;
                    }
                    yield return new WaitForSecondsRealtime(0.25f);
                }
                if (current == null || runtimeDungeon == null || runtimeDungeon.Generator == null ||
                    runtimeDungeon.Generator.IsGenerating)
                {
                    Fail("Actual StartMap dungeon did not finish generation at attempt " +
                         generationAttemptCount + ".");
                    yield break;
                }

                if (!TryClassifyGeneratedPairCandidate(
                        runtimeDungeon,
                        current,
                        out bool retryableGoalMiss,
                        out string candidateSummary,
                        out string candidateFailure))
                {
                    generationAttemptSummaries.Add("attempt=" + generationAttemptCount +
                                                   ";" + candidateSummary);
                    if (retryableGoalMiss)
                        continue;
                    Fail("Generated dungeon contract failed at attempt " + generationAttemptCount +
                         ": " + candidateFailure);
                    yield break;
                }

                generatedRoomCount = current.AllTiles.Count;
                generatedDungeon = current;
                if (!TryBindGeneratedMainPathPair(
                        runtimeDungeon,
                        out string candidatePlacementFailure))
                {
                    RestoreGeneratedPairState();
                    pairTransform.SetPositionAndRotation(originalPairPosition, originalPairRotation);
                    generatedDungeon = null;
                    generatedStartTile = null;
                    generatedAdministrativeTile = null;
                    generatedStartDoorway = null;
                    generatedAdministrativeDoorway = null;
                    generatedRoomCount = 0;
                    generationAttemptSummaries.Add("attempt=" + generationAttemptCount +
                                                   ";" + candidateSummary +
                                                   ";accepted=false;placement=" + candidatePlacementFailure);
                    if (IsRetryablePairPlacementFailure(candidatePlacementFailure))
                        continue;
                    Fail("Generated Start/Admin pair placement failed at attempt " +
                         generationAttemptCount + ": " + candidatePlacementFailure);
                    yield break;
                }
                generationAttemptSummaries.Add("attempt=" + generationAttemptCount +
                                               ";" + candidateSummary + ";accepted=true");
                break;
            }
            if (runtimeDungeon == null || generatedDungeon == null || generatedRoomCount <= 0)
            {
                Fail("Could not generate the exact production StartRoom/Admin pair within " +
                     generationAttemptCount + " bounded attempts and " + startupTimeoutSeconds +
                     " seconds. " + string.Join(" | ", generationAttemptSummaries));
                yield break;
            }

            dungeonReadyResult = "NOT_REQUIRED exact generated-tile binding; registry points are not an entry gate";
            localPlayerController = FindLocalPlayerController();
            localPlayerRoot = localPlayerController != null ? localPlayerController.transform.root : null;
            if (localPlayerController == null || localPlayerRoot == null || zoneManager == null)
            {
                Fail("Actual owner player disappeared after dungeon entry.");
                yield break;
            }

            if (!TryValidateGeneratedSurfaceContract(false, true, out string placementFailure) ||
                !TryValidateDoorwayAlignment(out placementFailure))
            {
                Fail("Generated Start/Admin overlay drifted before entry capture: " + placementFailure);
                yield break;
            }
            validationPairRoot.SetActive(true);
            yield return null;

            if (!TryResolveActualPlayerCamera(out string cameraFailure))
            {
                Fail(cameraFailure);
                yield break;
            }
            status = "ENTERING EXACT GENERATED START TILE WITH ACTUAL PLAYER";
            if (!TryMoveActualPlayerCameraToFrame(0, out string framingFailure))
            {
                Fail(framingFailure);
                yield break;
            }
            if (!TryApplyActualDungeonEnvironment(out string environmentFailure))
            {
                Fail(environmentFailure);
                yield break;
            }
            entryResult = "Entered generated Start tile via actual FPS player and production " +
                          "DungeonZoneManager.EnterDungeon";
            // DungeonZoneManager owns the final indoor RenderSettings in LateUpdate. Require
            // three consecutive completed render frames with the exact production contract;
            // a single transient match is not evidence-grade parity.
            status = "WAITING FOR 3 STABLE PRODUCTION END-OF-FRAME RENDER STATES";
            int stableRenderFrames = 0;
            string renderContractFailure = null;
            RenderSettingsSnapshot stableRenderSettings = default;
            while (Time.realtimeSinceStartup < deadline && stableRenderFrames < 3)
            {
                yield return new WaitForEndOfFrame();
                if (!TryValidateProductionIndoorRenderContract(
                        zoneManager, out renderContractFailure))
                {
                    stableRenderFrames = 0;
                    continue;
                }
                RenderSettingsSnapshot currentRenderSettings =
                    RenderSettingsSnapshot.CaptureCurrent();
                if (stableRenderFrames == 0 ||
                    !stableRenderSettings.ExactlyEquals(currentRenderSettings))
                {
                    stableRenderSettings = currentRenderSettings;
                    stableRenderFrames = 1;
                    continue;
                }
                stableRenderFrames++;
            }
            if (stableRenderFrames != 3)
            {
                Fail("Production indoor RenderSettings did not remain exact for three " +
                     "consecutive end-of-frame gates: " + renderContractFailure);
                yield break;
            }

            while (Time.realtimeSinceStartup < deadline &&
                   (!connectionDriver.TryValidateConfiguration(out _) ||
                    !doorShDriver.TryValidateConfiguration(out _) ||
                    !reflectionDriver.TryValidateConfiguration(out _) ||
                    !reflectionDriver.IsInitialized))
            {
                yield return null;
            }
            string driverFailure = null;
            string doorFailure = null;
            string reflectionFailure = null;
            bool driversValid = connectionDriver.TryValidateConfiguration(out driverFailure);
            bool doorValid = doorShDriver.TryValidateConfiguration(out doorFailure);
            bool reflectionValid = reflectionDriver.TryValidateConfiguration(out reflectionFailure);
            if (!driversValid || !doorValid || !reflectionValid ||
                !reflectionDriver.IsInitialized || connectionDriver.IsFaultLatched ||
                doorShDriver.IsFaultLatched || reflectionDriver.IsFaultLatched)
            {
                Fail("DPBB runtime drivers are not ready. Driver=" + driverFailure + " DoorSH=" +
                     doorFailure + " Reflection=" + reflectionFailure);
                yield break;
            }

            if (!TryCaptureSpotPolicyFingerprints(out string fingerprintFailure))
            {
                Fail("Could not capture StartMap spot-policy fingerprints: " + fingerprintFailure);
                yield break;
            }

            status = "WAITING TO FINALIZE READY";
            if (!TryValidateCaptureEnvironment(out string readyFailure))
            {
                Fail("READY environment contract did not validate: " + readyFailure);
                yield break;
            }

            status = "READY";
            Debug.Log("[DPBB StartMap Harness] READY rooms=" + generatedRoomCount +
                      " volume=" + assignedDungeonVolume.sharedProfile.name +
                      " camera=" + actualPlayerCamera.name +
                      " fingerprint=" + environmentFingerprint.Sha256 +
                      " spotPolicy=" + evidenceSpotPolicy, this);
        }

        private IEnumerator LoadActualStartMap()
        {
            Scene existing = SceneManager.GetSceneByPath(StartMapScenePath);
            if (existing.IsValid() && existing.isLoaded)
            {
                Fail("Actual StartMap was already loaded; the isolated harness requires a single authoritative StartMap instance.");
                yield break;
            }
            AsyncOperation load = SceneManager.LoadSceneAsync(StartMapScenePath, LoadSceneMode.Additive);
            if (load == null)
            {
                Fail("Could not begin additive load of actual StartMap.");
                yield break;
            }
            while (!load.isDone)
                yield return null;

            startMapScene = SceneManager.GetSceneByPath(StartMapScenePath);
            if (!startMapScene.IsValid() || !startMapScene.isLoaded ||
                !SceneManager.SetActiveScene(startMapScene))
            {
                Fail("Actual StartMap did not load or could not become the active scene.");
                yield break;
            }
        }

        public bool TrySetCaptureFrame(int frameIndex, out string failure)
        {
            if (!IsReady)
            {
                failure = "StartMap harness is not READY: " + status;
                return false;
            }
            if (!TryMoveActualPlayerCameraToFrame(frameIndex, out failure))
                return false;
            return TryValidateCaptureEnvironment(out failure);
        }

        /// <summary>
        /// Capture tools must call this before frames that change comparison policy. It resolves no
        /// substitute object: ORACLE_SPOT_OFF and HUMAN_SPOT_ON both operate only on the assigned
        /// FPSController.dungeonOnlyLight field captured at READY.
        /// </summary>
        public bool TryApplyEvidenceSpotPolicyForCapture(
            DungeonPortalBakedBasisEvidenceSpotPolicy policy,
            out string failure)
        {
            if (!IsReady)
            {
                failure = "StartMap harness is not READY: " + status;
                return false;
            }
            if (!TryApplyEvidenceSpotPolicy(policy, out failure))
                return false;
            return TryValidateCaptureEnvironment(out failure);
        }

        public bool TryValidateCaptureEnvironment(out string failure)
        {
            if (!IsReady && !string.Equals(status, "WAITING TO FINALIZE READY", StringComparison.Ordinal))
            {
                failure = "StartMap harness is not READY: " + status;
                return false;
            }
            DungeonPortalBakedBasisRuntimeEnvironmentFingerprint expectedFingerprint =
                ResolveEnvironmentFingerprint(evidenceSpotPolicy);
            if (expectedFingerprint == null || actualPlayerCamera == null ||
                assignedDungeonVolume == null || zoneManager == null || localPlayerController == null ||
                localPlayerRoot == null || validationPairRoot == null || !validationPairRoot.activeInHierarchy)
            {
                failure = "StartMap harness has an incomplete READY binding.";
                return false;
            }
            if (!startMapScene.IsValid() || !startMapScene.isLoaded ||
                SceneManager.GetActiveScene() != startMapScene || !zoneManager.IsInDungeon ||
                assignedDungeonVolume.sharedProfile == null ||
                !string.Equals(assignedDungeonVolume.sharedProfile.name,
                    RequiredDungeonVolumeProfileName, StringComparison.Ordinal) ||
                !assignedDungeonVolume.enabled || assignedDungeonOnlyLight == null ||
                assignedDungeonOnlyLight.enabled !=
                (evidenceSpotPolicy == DungeonPortalBakedBasisEvidenceSpotPolicy.HUMAN_SPOT_ON))
            {
                failure = "Actual StartMap active-scene, dungeon-zone, player-camera, Volume, or player-light contract drifted.";
                return false;
            }
            if (!TryResolveUniqueProductionZoneManager(
                    out DungeonZoneManager currentZoneManager,
                    out string zoneFailure) ||
                currentZoneManager != zoneManager)
            {
                failure = "Production StartMap DungeonZoneManager identity drifted: " + zoneFailure;
                return false;
            }
            if (!TryValidateProductionIndoorRenderContract(zoneManager, out failure))
                return false;
            string driverFailure = null;
            string doorFailure = null;
            string reflectionFailure = null;
            bool driversValid = connectionDriver.TryValidateConfiguration(out driverFailure);
            bool doorValid = doorShDriver.TryValidateConfiguration(out doorFailure);
            bool reflectionValid = reflectionDriver.TryValidateConfiguration(out reflectionFailure);
            if (!driversValid || !doorValid || !reflectionValid ||
                connectionDriver.IsFaultLatched || doorShDriver.IsFaultLatched ||
                reflectionDriver.IsFaultLatched || !reflectionDriver.IsInitialized)
            {
                failure = "DPBB driver contract drifted. Driver=" + driverFailure + " DoorSH=" +
                          doorFailure + " Reflection=" + reflectionFailure;
                return false;
            }
            if (!TryValidateGeneratedSurfaceContract(true, false, out failure) ||
                !TryValidateDoorwayAlignment(out failure) ||
                !TryValidateFrameSpatialContract(currentFrameIndex, out failure))
                return false;
            return expectedFingerprint.TryValidateCurrent(
                actualPlayerCamera,
                assignedDungeonVolume,
                validationPairRoot,
                GetReferenceFrames(),
                evidenceSpotPolicy,
                currentFrameIndex,
                out failure);
        }

        private bool TryValidateAuthoring(out string failure)
        {
            Camera[] frames = GetReferenceFrames();
            if (validationPairRoot == null || connectionDriver == null || doorShDriver == null ||
                reflectionDriver == null || frames.Length != 2 || frames[0] == null || frames[1] == null ||
                frames[0] == frames[1] || validationStartRoom == null ||
                validationAdministrativeRoom == null || validationStartDoorway == null ||
                validationAdministrativeDoorway == null || requiredDungeonFlow == null ||
                requiredStartTileSet == null || requiredAdministrativeTileSet == null ||
                requiredStartTileSet == requiredAdministrativeTileSet ||
                requiredStartPrefab == null || requiredAdministrativePrefab == null ||
                requiredStartRoomBasis == null || requiredAdministrativeRoomBasis == null ||
                requiredCompositionShader == null ||
                !string.Equals(requiredCompositionShader.name,
                    "Hidden/DungeonPortalBakedBasisPoC/Compose", StringComparison.Ordinal) ||
                requiredStartPrefab == requiredAdministrativePrefab ||
                !TileSetContainsExactPrefab(requiredStartTileSet, requiredStartPrefab) ||
                !TileSetContainsExactPrefab(requiredAdministrativeTileSet,
                    requiredAdministrativePrefab))
            {
                failure = "StartMap DPBB harness has incomplete pair, camera, or driver references.";
                return false;
            }
            Component[] components = validationPairRoot.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                string typeName = component != null ? component.GetType().FullName : string.Empty;
                if (!string.IsNullOrEmpty(typeName) &&
                    (typeName.StartsWith("DungeonAdjacentLightingPoC.", StringComparison.Ordinal) ||
                     typeName.IndexOf("DungeonAdjacentLightmapExtension", StringComparison.Ordinal) >= 0))
                {
                    failure = "Rejected V21 adjacent-lighting overlay is present in the DPBB pair: " + typeName;
                    return false;
                }
            }
            if (frames[0].enabled || frames[1].enabled)
            {
                failure = "DPBB reference cameras must stay disabled; actual player camera is mandatory.";
                return false;
            }
            failure = null;
            return true;
        }

        private bool TryResolveActualPlayerCamera(out string failure)
        {
            Camera[] candidates = localPlayerController.GetComponentsInChildren<Camera>(true);
            Camera result = null;
            int count = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                Camera candidate = candidates[i];
                if (candidate == null || !candidate.enabled || !candidate.gameObject.activeInHierarchy)
                    continue;
                result = candidate;
                count++;
            }
            if (count != 1 || result == null || !result.transform.IsChildOf(localPlayerRoot))
            {
                failure = "Actual local-owner FPSController must expose exactly one enabled child camera; fallback camera use is forbidden.";
                return false;
            }
            actualPlayerCamera = result;
            failure = null;
            return true;
        }

        private bool TryApplyActualDungeonEnvironment(out string failure)
        {
            zoneManager.EnterDungeon(localPlayerRoot);
            localPlayerController.SetDungeonPostProcessing(true);
            localPlayerController.SetDungeonRenderLayerServerRpc(true);
            assignedDungeonVolume = ResolveAssignedDungeonVolume(localPlayerController);
            assignedDungeonOnlyLight = ResolveAssignedDungeonOnlyLight(localPlayerController);
            if (assignedDungeonVolume == null || assignedDungeonOnlyLight == null ||
                assignedDungeonVolume.sharedProfile == null ||
                !string.Equals(assignedDungeonVolume.sharedProfile.name,
                    RequiredDungeonVolumeProfileName, StringComparison.Ordinal))
            {
                failure = "FPSController assigned dungeon Volume/light is missing or Volume is not '" +
                          RequiredDungeonVolumeProfileName + "'.";
                return false;
            }
            assignedDungeonVolume.enabled = true;
            if (!dungeonOnlyLightSnapshotCaptured)
            {
                originalDungeonOnlyLightEnabled = assignedDungeonOnlyLight.enabled;
                dungeonOnlyLightSnapshotCaptured = true;
            }
            if (!TryApplyEvidenceSpotPolicy(evidenceSpotPolicy, out failure) ||
                !zoneManager.IsInDungeon || !assignedDungeonVolume.enabled)
            {
                failure ??= "Actual StartMap dungeon environment did not apply deterministically.";
                return false;
            }
            failure = null;
            return true;
        }

        private bool TryMoveActualPlayerCameraToFrame(int frameIndex, out string failure)
        {
            Camera[] frames = GetReferenceFrames();
            if (frameIndex < 0 || frameIndex >= frames.Length || frames[frameIndex] == null ||
                actualPlayerCamera == null || localPlayerRoot == null || frames[frameIndex].enabled)
            {
                failure = "Cannot align the actual player camera to the requested disabled reference frame.";
                return false;
            }
            Camera reference = frames[frameIndex];
            CharacterController[] controllers = localPlayerRoot.GetComponentsInChildren<CharacterController>(true);
            bool[] enabled = new bool[controllers.Length];
            for (int i = 0; i < controllers.Length; i++)
            {
                enabled[i] = controllers[i] != null && controllers[i].enabled;
                if (enabled[i]) controllers[i].enabled = false;
            }
            try
            {
                Quaternion rotationDelta = reference.transform.rotation *
                                           Quaternion.Inverse(actualPlayerCamera.transform.rotation);
                localPlayerRoot.rotation = rotationDelta * localPlayerRoot.rotation;
                localPlayerRoot.position += reference.transform.position - actualPlayerCamera.transform.position;
                Physics.SyncTransforms();
            }
            finally
            {
                for (int i = 0; i < controllers.Length; i++)
                    if (controllers[i] != null) controllers[i].enabled = enabled[i];
            }
            if (Vector3.Distance(actualPlayerCamera.transform.position, reference.transform.position) > 0.001f ||
                Quaternion.Angle(actualPlayerCamera.transform.rotation, reference.transform.rotation) > 0.01f)
            {
                failure = "Actual player camera could not be aligned to the fixed reference frame.";
                return false;
            }
            zoneManager.EnterDungeon(localPlayerRoot);
            currentFrameIndex = frameIndex;
            return TryValidateFrameSpatialContract(frameIndex, out failure);
        }

        private bool TryValidateProductionMapList(
            NetworkDungeonController networkDungeon,
            out string failure)
        {
            if (networkDungeon == null || networkDungeon.MapList == null ||
                networkDungeon.MapList.Count != 1 ||
                !networkDungeon.MapList.TryGetFlow(0, out DungeonFlow onlyFlow) ||
                onlyFlow != requiredDungeonFlow)
            {
                failure = "Actual NetworkDungeonController must expose exactly one MapList entry " +
                          "and it must be the required production V2 DungeonFlow.";
                return false;
            }
            failure = null;
            return true;
        }

        private bool TryClassifyGeneratedPairCandidate(
            RuntimeDungeon runtimeDungeon,
            Dungeon dungeon,
            out bool retryableGoalMiss,
            out string summary,
            out string failure)
        {
            retryableGoalMiss = false;
            string goalName = dungeon != null && dungeon.MainPathTiles != null &&
                              dungeon.MainPathTiles.Count > 1 &&
                              dungeon.MainPathTiles[1] != null &&
                              dungeon.MainPathTiles[1].Prefab != null
                ? dungeon.MainPathTiles[1].Prefab.name
                : "null";
            summary = "generationResult=" + generationResult.Replace(';', ',') +
                      ";goalPrefab=" + goalName;
            if (runtimeDungeon == null || runtimeDungeon.Generator == null || dungeon == null ||
                runtimeDungeon.Generator.CurrentDungeon != dungeon ||
                runtimeDungeon.Generator.DungeonFlow != requiredDungeonFlow ||
                dungeon.DungeonFlow != requiredDungeonFlow)
            {
                failure = "Generated dungeon is not the current required production V2 flow.";
                return false;
            }
            if (dungeon.MainPathTiles == null || dungeon.MainPathTiles.Count < 2 ||
                dungeon.AllTiles == null || dungeon.AllTiles.Count < 2)
            {
                failure = "Production validation requires at least the Start and first goal main-path tiles.";
                return false;
            }

            Tile startTile = dungeon.MainPathTiles[0];
            Tile goalTile = dungeon.MainPathTiles[1];
            if (!TryValidateGeneratedTile(
                    startTile,
                    requiredStartTileSet,
                    requiredStartPrefab,
                    0,
                    out failure))
            {
                return false;
            }
            if (!TryValidateGeneratedTileSetMember(
                    goalTile,
                    requiredAdministrativeTileSet,
                    1,
                    out failure))
            {
                return false;
            }
            if (goalTile.Prefab != requiredAdministrativePrefab)
            {
                retryableGoalMiss = true;
                summary += ";accepted=false;reason=non-admin-production-goal";
                failure = "Generated production goal was " + goalName + ", not the required Admin room.";
                return false;
            }
            if (!TryValidateGeneratedTile(
                    goalTile,
                    requiredAdministrativeTileSet,
                    requiredAdministrativePrefab,
                    1,
                    out failure))
            {
                return false;
            }
            failure = null;
            return true;
        }

        private bool TryBindGeneratedMainPathPair(
            RuntimeDungeon runtimeDungeon,
            out string failure)
        {
            Dungeon dungeon = runtimeDungeon != null && runtimeDungeon.Generator != null
                ? runtimeDungeon.Generator.CurrentDungeon
                : null;
            if (dungeon == null || dungeon != generatedDungeon ||
                runtimeDungeon.Generator.DungeonFlow != requiredDungeonFlow ||
                dungeon.DungeonFlow != requiredDungeonFlow)
            {
                failure = "Generated dungeon is not backed by the required production DungeonFlow asset.";
                return false;
            }
            if (dungeon.MainPathTiles == null || dungeon.MainPathTiles.Count < 2 ||
                dungeon.AllTiles == null || dungeon.AllTiles.Count < 2)
            {
                failure = "Production validation requires at least the generated Start/Admin main-path pair.";
                return false;
            }

            Tile startTile = dungeon.MainPathTiles[0];
            Tile administrativeTile = dungeon.MainPathTiles[1];
            if (!TryValidateGeneratedTile(
                    startTile,
                    requiredStartTileSet,
                    requiredStartPrefab,
                    0,
                    out failure) ||
                !TryValidateGeneratedTile(
                    administrativeTile,
                    requiredAdministrativeTileSet,
                    requiredAdministrativePrefab,
                    1,
                    out failure))
            {
                return false;
            }

            Doorway startDoor = null;
            Doorway administrativeDoor = null;
            int matchingConnections = 0;
            for (int i = 0; i < dungeon.Connections.Count; i++)
            {
                DoorwayConnection connection = dungeon.Connections[i];
                if (connection == null || connection.A == null || connection.B == null)
                    continue;
                bool forward = connection.A.Tile == startTile && connection.B.Tile == administrativeTile;
                bool reverse = connection.B.Tile == startTile && connection.A.Tile == administrativeTile;
                if (!forward && !reverse)
                    continue;
                matchingConnections++;
                startDoor = forward ? connection.A : connection.B;
                administrativeDoor = forward ? connection.B : connection.A;
            }
            if (matchingConnections != 1 || startDoor == null || administrativeDoor == null ||
                startDoor.ConnectedDoorway != administrativeDoor ||
                administrativeDoor.ConnectedDoorway != startDoor)
            {
                failure = "Generated StartRoom/Admin main-path connection is missing or ambiguous; count=" +
                          matchingConnections + ".";
                return false;
            }

            if (!TryApplyRigidDoorwayAlignmentForTest(
                    validationPairRoot.transform,
                    validationStartDoorway,
                    startDoor.transform,
                    out failure))
            {
                return false;
            }

            generatedStartTile = startTile;
            generatedAdministrativeTile = administrativeTile;
            generatedStartDoorway = startDoor;
            generatedAdministrativeDoorway = administrativeDoor;
            generatedNonTargetTileBounds.Clear();
            for (int i = 0; i < dungeon.AllTiles.Count; i++)
            {
                Tile tile = dungeon.AllTiles[i];
                if (tile != null && tile != startTile && tile != administrativeTile)
                    generatedNonTargetTileBounds.Add(tile.Bounds);
            }
            if (!TryValidateDoorwayAlignment(out failure))
            {
                RestoreGeneratedPairState();
                return false;
            }
            if (!TryValidateReferenceCameraSpatialContracts(out failure))
            {
                failure = "Generated pair spatial mismatch: " + failure;
                RestoreGeneratedPairState();
                return false;
            }
            if (!TryRebindRuntimeToGeneratedPair(out failure))
            {
                RestoreGeneratedPairState();
                return false;
            }
            return TryValidateGeneratedSurfaceContract(false, true, out failure);
        }

        private bool TryRebindRuntimeToGeneratedPair(out string failure)
        {
            failure = null;
            if (validationPairRoot.activeInHierarchy || generatedRendererSnapshots.Count != 0 ||
                validationRendererSnapshots.Count != 0 || generatedProductionProbes.Count != 0)
            {
                failure = "Generated-room runtime rebind requires a clean inactive validation pair.";
                return false;
            }

            DungeonPortalBakedBasisRoomCompositor startCompositor =
                connectionDriver.StartRoomCompositor;
            DungeonPortalBakedBasisRoomCompositor administrativeCompositor =
                connectionDriver.AdministrativeRoomCompositor;
            if (startCompositor == null || administrativeCompositor == null ||
                startCompositor.IsActive || administrativeCompositor.IsActive ||
                doorShDriver.IsActive || doorShDriver.BoundRendererCount != 0 ||
                reflectionDriver.IsInitialized)
            {
                failure = "DPBB drivers were already active before generated-room rebinding.";
                return false;
            }

            DungeonPortalBakedRoomBasisData startBasis = requiredStartRoomBasis;
            DungeonPortalBakedRoomBasisData administrativeBasis =
                requiredAdministrativeRoomBasis;
            string startBasisFailure = null;
            string administrativeBasisFailure = null;
            bool startBasisValid = startBasis != null &&
                                   startBasis.TryValidateDefinition(out startBasisFailure);
            bool administrativeBasisValid = administrativeBasis != null &&
                administrativeBasis.TryValidateDefinition(out administrativeBasisFailure);
            if (!startBasisValid || !administrativeBasisValid)
            {
                failure = "Generated-room basis definition is invalid. Start=" +
                          startBasisFailure + " Administrative=" + administrativeBasisFailure;
                return false;
            }
            if (!TryResolveDoorContract(
                    startCompositor,
                    administrativeCompositor,
                    out string startDoorId,
                    out string administrativeDoorId,
                    out failure))
            {
                return false;
            }
            Shader startShader = requiredCompositionShader;
            if (startShader == null || !string.Equals(startShader.name,
                    "Hidden/DungeonPortalBakedBasisPoC/Compose", StringComparison.Ordinal))
            {
                failure = "The exact serialized DPBB composition shader could not be preserved.";
                return false;
            }

            bool startBindingsValid =
                DungeonPortalBakedBasisRoomCompositor.TryCreateExplicitBindings(
                    startBasis,
                    generatedStartTile.transform,
                    out DungeonPortalBakedBasisRoomCompositor.ExplicitRendererBinding[] startBindings,
                    out Renderer[] startExtras,
                    out string startBindingFailure);
            bool administrativeBindingsValid =
                DungeonPortalBakedBasisRoomCompositor.TryCreateExplicitBindings(
                    administrativeBasis,
                    generatedAdministrativeTile.transform,
                    out DungeonPortalBakedBasisRoomCompositor.ExplicitRendererBinding[]
                        administrativeBindings,
                    out Renderer[] administrativeExtras,
                    out string administrativeBindingFailure);
            if (!startBindingsValid || !administrativeBindingsValid)
            {
                failure = "Actual generated V2 renderer keys do not match the baked basis. Start=" +
                          startBindingFailure + " Administrative=" +
                          administrativeBindingFailure;
                return false;
            }
            if (!TrySnapshotGeneratedRenderers(startBindings, administrativeBindings, out failure))
                return false;
            if (!TryValidateUnboundExtras(startExtras, "Start", out failure) ||
                !TryValidateUnboundExtras(administrativeExtras, "Administrative", out failure))
            {
                return false;
            }

            if (!TryGetUniqueComponentInChildren(
                    generatedStartTile.transform,
                    out DungeonTileLightmapSwitcher startSwitcher,
                    out failure) ||
                !TryGetUniqueComponentInChildren(
                    generatedAdministrativeTile.transform,
                    out DungeonTileLightmapSwitcher administrativeSwitcher,
                    out failure))
            {
                failure = "Actual generated V2 switcher contract failed: " + failure;
                return false;
            }
            if (!TryRegisterAllowedMaterialVariants(startSwitcher, out failure) ||
                !TryRegisterAllowedMaterialVariants(administrativeSwitcher, out failure))
            {
                failure = "Production emission-material contract failed: " + failure;
                return false;
            }

            GameObject startDoorInstance = generatedStartDoorway.UsedDoorPrefabInstance;
            GameObject administrativeDoorInstance =
                generatedAdministrativeDoorway.UsedDoorPrefabInstance;
            if ((startDoorInstance == null && administrativeDoorInstance == null) ||
                (startDoorInstance != null && administrativeDoorInstance != null &&
                 startDoorInstance != administrativeDoorInstance))
            {
                failure = "The generated reciprocal doorway does not resolve one unique spawned door.";
                return false;
            }
            GameObject spawnedDoorInstance = startDoorInstance != null
                ? startDoorInstance
                : administrativeDoorInstance;
            DungeonPortalDoorAngleSource angleSource = connectionDriver.DoorAngleSource;
            if (angleSource == null ||
                !TryResolveActualDoorLeaf(
                    spawnedDoorInstance.transform,
                    out Transform actualDoorLeaf,
                    out Vector3 hingeAxis,
                    out float openAngleDegrees,
                    out failure) ||
                hingeAxis.sqrMagnitude <= Mathf.Epsilon ||
                Mathf.Abs(openAngleDegrees) <= 0.001f)
            {
                failure = "Actual generated door-leaf angle contract could not be rebound: " + failure;
                return false;
            }
            actualGeneratedDoorLeaf = actualDoorLeaf;

            if (!TryPrepareValidationOnlyObjects(out failure) ||
                !TryPrepareActualDoorReceivers(actualDoorLeaf, out failure) ||
                !TryPrepareReflectionRebind(out ReflectionRebindConfiguration reflections,
                    out failure))
            {
                return false;
            }

            float startPower = connectionDriver.StartPower01;
            float administrativePower = connectionDriver.AdministrativePower01;
            if (!TryReadPrivateField(connectionDriver, "adjacentTransportEnabled",
                    out bool adjacentTransportEnabled) || !adjacentTransportEnabled)
            {
                failure = "DPBB generated-room rebind requires adjacent transport enabled.";
                return false;
            }
            originalConnectionAdjacentEnabled = adjacentTransportEnabled;
            connectionAdjacentSnapshotCaptured = true;
            if (!TryReadPrivateField(connectionDriver, "powerTransitionSeconds",
                    out float powerTransitionSeconds) ||
                !TryReadPrivateField(connectionDriver, "apertureTransitionSeconds",
                    out float apertureTransitionSeconds))
            {
                failure = "DPBB transition timing could not be preserved during runtime rebind.";
                return false;
            }

            if (!TryCloneIncomingDoorStates(
                    startCompositor.IncomingDoors,
                    administrativeBasis,
                    out DungeonPortalBakedBasisRoomCompositor.IncomingDoorState[] startIncoming,
                    out failure) ||
                !TryCloneIncomingDoorStates(
                    administrativeCompositor.IncomingDoors,
                    startBasis,
                    out DungeonPortalBakedBasisRoomCompositor.IncomingDoorState[]
                        administrativeIncoming,
                    out failure))
            {
                return false;
            }

            startCompositor.ConfigureAuthoring(
                startBasis,
                generatedStartTile.transform,
                startShader,
                startBindings,
                startIncoming,
                startCompositor.BasePower01,
                true,
                0.5f);
            administrativeCompositor.ConfigureAuthoring(
                administrativeBasis,
                generatedAdministrativeTile.transform,
                startShader,
                administrativeBindings,
                administrativeIncoming,
                administrativeCompositor.BasePower01,
                true,
                0.5f);
            angleSource.Configure(
                actualDoorLeaf,
                actualDoorLeaf.localRotation,
                hingeAxis,
                openAngleDegrees);
            connectionDriver.ConfigureAuthoring(
                connectionDriver.ConnectionId,
                startCompositor,
                administrativeCompositor,
                startBasis,
                administrativeBasis,
                startDoorId,
                administrativeDoorId,
                angleSource,
                startSwitcher,
                administrativeSwitcher,
                startPower,
                administrativePower,
                powerTransitionSeconds,
                apertureTransitionSeconds);
            doorShDriver.ConfigureAuthoring(
                connectionDriver,
                actualDoorLeaf,
                generatedStartTile.transform,
                generatedAdministrativeTile.transform,
                startBasis,
                administrativeBasis,
                startDoorId,
                administrativeDoorId);
            reflectionDriver.ConfigureAuthoring(
                connectionDriver,
                reflections.StartStableId,
                reflections.StartOwnedProbe,
                reflections.StartProfile,
                reflections.AdministrativeStableIds,
                reflections.AdministrativeOwnedProbes,
                reflections.AdministrativeProfiles,
                generatedProductionProbes.ToArray());

            bool driverValid = connectionDriver.TryValidateConfiguration(out string driverFailure);
            bool doorValid = doorShDriver.TryValidateConfiguration(out string doorFailure);
            bool reflectionValid = reflectionDriver.TryValidateConfiguration(
                out string reflectionFailure);
            if (!driverValid || !doorValid || !reflectionValid)
            {
                failure = "Generated-room driver rebind is invalid. Driver=" + driverFailure +
                          " DoorSH=" + doorFailure + " Reflection=" + reflectionFailure;
                return false;
            }

            if (!connectionDriver.SetImmediateForEvidence(
                    startPower, administrativePower, 1f, out string openPreflightFailure) ||
                connectionDriver.IsFaultLatched)
            {
                failure = "Generated V2 open-portal endpoint preflight failed: " +
                          openPreflightFailure;
                return false;
            }
            if (!connectionDriver.SetImmediateForEvidence(
                    startPower, administrativePower, 0f, out string closedPreflightFailure) ||
                connectionDriver.IsFaultLatched || startCompositor.IsActive ||
                administrativeCompositor.IsActive)
            {
                failure = "Generated V2 D0 exact-restoration preflight failed: " +
                          closedPreflightFailure;
                return false;
            }
            connectionDriver.ClearEvidenceOverride();

            failure = null;
            return true;
        }

        private static bool TryValidateUnboundExtras(
            Renderer[] extras,
            string roomLabel,
            out string failure)
        {
            if (extras == null)
            {
                failure = roomLabel + " canonical binding returned a null extras array.";
                return false;
            }
            int lightmapCount = LightmapSettings.lightmaps != null
                ? LightmapSettings.lightmaps.Length
                : 0;
            for (int i = 0; i < extras.Length; i++)
            {
                Renderer extra = extras[i];
                if (extra == null)
                {
                    failure = roomLabel + " unbound renderer entry " + i + " is missing.";
                    return false;
                }
                if (extra.lightmapIndex >= 0 && extra.lightmapIndex < lightmapCount)
                {
                    failure = roomLabel + " has a lightmapped renderer outside the V2 basis: " +
                              extra.name + ". Regenerate the basis instead of leaving a static " +
                              "surface unresponsive.";
                    return false;
                }
            }
            failure = null;
            return true;
        }

        private bool TryResolveDoorContract(
            DungeonPortalBakedBasisRoomCompositor startCompositor,
            DungeonPortalBakedBasisRoomCompositor administrativeCompositor,
            out string startDoorId,
            out string administrativeDoorId,
            out string failure)
        {
            startDoorId = null;
            administrativeDoorId = null;
            DungeonPortalBakedBasisRoomCompositor.IncomingDoorState[] startIncoming =
                startCompositor.IncomingDoors;
            DungeonPortalBakedBasisRoomCompositor.IncomingDoorState[] administrativeIncoming =
                administrativeCompositor.IncomingDoors;
            if (startIncoming.Length != 1 || administrativeIncoming.Length != 1 ||
                startIncoming[0] == null || administrativeIncoming[0] == null ||
                string.IsNullOrWhiteSpace(connectionDriver.ConnectionId))
            {
                failure = "DPBB validation pair must expose one reciprocal incoming-door state.";
                return false;
            }

            DungeonPortalBakedBasisRoomCompositor.IncomingDoorState intoStart = startIncoming[0];
            DungeonPortalBakedBasisRoomCompositor.IncomingDoorState intoAdministrative =
                administrativeIncoming[0];
            string requiredIntoStart = connectionDriver.ConnectionId + "/AdministrativeToStart";
            string requiredIntoAdministrative =
                connectionDriver.ConnectionId + "/StartToAdministrative";
            startDoorId = intoStart.ReceiverDoorId;
            administrativeDoorId = intoAdministrative.ReceiverDoorId;
            if (!string.Equals(intoStart.ConnectionId, requiredIntoStart,
                    StringComparison.Ordinal) ||
                !string.Equals(intoAdministrative.ConnectionId, requiredIntoAdministrative,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(startDoorId) ||
                string.IsNullOrWhiteSpace(administrativeDoorId) ||
                !string.Equals(intoStart.SourceDoorId, administrativeDoorId,
                    StringComparison.Ordinal) ||
                !string.Equals(intoAdministrative.SourceDoorId, startDoorId,
                    StringComparison.Ordinal))
            {
                failure = "DPBB incoming-door IDs are not one exact reciprocal connection.";
                return false;
            }
            failure = null;
            return true;
        }

        private static bool TryCloneIncomingDoorStates(
            DungeonPortalBakedBasisRoomCompositor.IncomingDoorState[] source,
            DungeonPortalBakedRoomBasisData replacementSourceBasis,
            out DungeonPortalBakedBasisRoomCompositor.IncomingDoorState[] result,
            out string failure)
        {
            result = Array.Empty<DungeonPortalBakedBasisRoomCompositor.IncomingDoorState>();
            if (source == null || source.Length != 1 || source[0] == null ||
                replacementSourceBasis == null)
            {
                failure = "Cannot rebuild the exact incoming-door state for the V2 basis.";
                return false;
            }
            DungeonPortalBakedBasisRoomCompositor.IncomingDoorState original = source[0];
            var clone = new DungeonPortalBakedBasisRoomCompositor.IncomingDoorState();
            clone.ConfigureAuthoring(
                original.ConnectionId,
                original.ReceiverDoorId,
                replacementSourceBasis,
                original.SourceDoorId,
                original.SourcePower01,
                original.Aperture01,
                original.ResponseScale,
                original.Contributes);
            result = new[] { clone };
            failure = null;
            return true;
        }

        private bool TrySnapshotGeneratedRenderers(
            DungeonPortalBakedBasisRoomCompositor.ExplicitRendererBinding[] startBindings,
            DungeonPortalBakedBasisRoomCompositor.ExplicitRendererBinding[] administrativeBindings,
            out string failure)
        {
            var bound = new HashSet<Renderer>();
            DungeonPortalBakedBasisRoomCompositor.ExplicitRendererBinding[][] bindingSets =
                { startBindings, administrativeBindings };
            for (int setIndex = 0; setIndex < bindingSets.Length; setIndex++)
            {
                DungeonPortalBakedBasisRoomCompositor.ExplicitRendererBinding[] bindings =
                    bindingSets[setIndex];
                for (int i = 0; i < bindings.Length; i++)
                {
                    Renderer renderer = bindings[i] != null ? bindings[i].Renderer : null;
                    if (renderer == null || !bound.Add(renderer))
                    {
                        failure = "Generated V2 basis contains a missing or duplicate renderer binding.";
                        return false;
                    }
                }
            }

            var seen = new HashSet<Renderer>();
            Tile[] tiles = { generatedStartTile, generatedAdministrativeTile };
            for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
            {
                Renderer[] renderers = tiles[tileIndex].GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    failure = "Generated V2 target tile has no renderer surface.";
                    return false;
                }
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null || !seen.Add(renderer))
                        continue;
                    generatedRendererSnapshots.Add(new RendererSnapshot(renderer));
                    if (bound.Contains(renderer))
                        generatedBasisBoundRenderers.Add(renderer);
                }
            }
            if (generatedBasisBoundRenderers.Count != bound.Count)
            {
                failure = "A canonical renderer binding escaped the exact generated tile roots.";
                return false;
            }
            failure = null;
            return true;
        }

        private static bool TryGetUniqueComponentInChildren<T>(
            Transform root,
            out T result,
            out string failure) where T : Component
        {
            result = null;
            T[] candidates = root != null
                ? root.GetComponentsInChildren<T>(true)
                : Array.Empty<T>();
            if (candidates.Length != 1 || candidates[0] == null)
            {
                failure = "Expected exactly one " + typeof(T).Name + "; count=" +
                          candidates.Length + ".";
                return false;
            }
            result = candidates[0];
            failure = null;
            return true;
        }

        private bool TryRegisterAllowedMaterialVariants(
            DungeonTileLightmapSwitcher switcher,
            out string failure)
        {
            if (switcher == null)
            {
                failure = "Production lightmap switcher is missing.";
                return false;
            }
            if (!TryGetUniqueComponentInChildren(
                    switcher.transform,
                    out DungeonTilePowerBakeSet bakeSet,
                    out failure))
            {
                return false;
            }
            var snapshotByRenderer = new Dictionary<Renderer, RendererSnapshot>();
            for (int i = 0; i < generatedRendererSnapshots.Count; i++)
                snapshotByRenderer[generatedRendererSnapshots[i].Renderer] =
                    generatedRendererSnapshots[i];

            var renderersByPath = new Dictionary<string, List<Renderer>>(StringComparer.Ordinal);
            Renderer[] renderers = switcher.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;
                string path = GetRelativePath(switcher.transform, renderer.transform);
                if (!renderersByPath.TryGetValue(path, out List<Renderer> bucket))
                {
                    bucket = new List<Renderer>();
                    renderersByPath.Add(path, bucket);
                }
                bucket.Add(renderer);
            }

            DungeonTilePowerBakeSet.EmissionMaterialEntry[] entries =
                bakeSet.EmissionMaterialEntries;
            for (int i = 0; i < entries.Length; i++)
            {
                DungeonTilePowerBakeSet.EmissionMaterialEntry entry = entries[i];
                if (string.IsNullOrWhiteSpace(entry.relativePath) ||
                    !renderersByPath.TryGetValue(entry.relativePath, out List<Renderer> bucket) ||
                    bucket.Count == 0)
                {
                    failure = "Emission entry " + i + " has no exact renderer path.";
                    return false;
                }
                if (entry.rendererBucketIndex < 0 ||
                    entry.rendererBucketIndex >= bucket.Count)
                {
                    failure = "Emission entry " + i + " has an invalid renderer bucket index.";
                    return false;
                }
                Renderer renderer = bucket[entry.rendererBucketIndex];
                if (!snapshotByRenderer.TryGetValue(renderer, out RendererSnapshot snapshot) ||
                    entry.materialIndex < 0 ||
                    entry.materialIndex >= renderer.sharedMaterials.Length)
                {
                    failure = "Emission entry " + i + " has an invalid renderer/material slot.";
                    return false;
                }
                snapshot.AddAllowedMaterial(entry.materialIndex, entry.power100Material);
                snapshot.AddAllowedMaterial(entry.materialIndex, entry.power00Material);
            }
            failure = null;
            return true;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == target)
                return string.Empty;
            var names = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return current == root ? string.Join("/", names) : string.Empty;
        }

        private static bool TryResolveActualDoorLeaf(
            Transform actualDoorRoot,
            out Transform actualDoorLeaf,
            out Vector3 hingeAxis,
            out float openAngleDegrees,
            out string failure)
        {
            actualDoorLeaf = null;
            hingeAxis = Vector3.zero;
            openAngleDegrees = 0f;
            if (actualDoorRoot == null)
            {
                failure = "Spawned production door root is missing.";
                return false;
            }
            global::Door[] candidates = actualDoorRoot.GetComponentsInChildren<global::Door>(true);
            if (candidates.Length != 1 || candidates[0] == null)
            {
                failure = "Spawned doorway must expose exactly one production Door; count=" +
                          candidates.Length + ".";
                return false;
            }
            global::Door door = candidates[0];
            if (door.doorMovement != global::Door.doorType.Regular || door.applyRotationFix ||
                door.IsOpen || door.IsMoving ||
                door.GetComponentsInChildren<DungeonDoorProbeRendererGroup>(true).Length == 0)
            {
                failure = "Spawned production door must be closed, stationary, regular, " +
                          "non-rotation-fixed, and own split renderer groups.";
                return false;
            }
            switch (door.rotationOrientation)
            {
                case global::Door.rotOrient.X_Axis_Up:
                    hingeAxis = Vector3.right;
                    break;
                case global::Door.rotOrient.Y_Axis_Up:
                    hingeAxis = Vector3.up;
                    break;
                case global::Door.rotOrient.Z_Axis_Up:
                    hingeAxis = Vector3.forward;
                    break;
                default:
                    failure = "Spawned production door has an unsupported hinge orientation.";
                    return false;
            }
            actualDoorLeaf = door.transform;
            openAngleDegrees = door.doorOpenAngle;
            failure = null;
            return true;
        }

        private bool TryPrepareValidationOnlyObjects(out string failure)
        {
            Renderer[] validationRenderers =
                validationPairRoot.GetComponentsInChildren<Renderer>(true);
            if (validationRenderers.Length == 0)
            {
                failure = "Validation pair exposes no visual renderers to hide.";
                return false;
            }
            var seen = new HashSet<Renderer>();
            for (int i = 0; i < validationRenderers.Length; i++)
            {
                Renderer renderer = validationRenderers[i];
                if (renderer == null || !seen.Add(renderer))
                    continue;
                var snapshot = new RendererSnapshot(renderer);
                validationRendererSnapshots.Add(snapshot);
                snapshot.HideForValidation();
            }

            DungeonTileLightmapSwitcher[] switchers =
                validationPairRoot.GetComponentsInChildren<DungeonTileLightmapSwitcher>(true);
            if (switchers.Length != 2)
            {
                failure = "Validation-only pair must expose exactly two legacy switchers; count=" +
                          switchers.Length + ".";
                return false;
            }
            for (int i = 0; i < switchers.Length; i++)
            {
                var snapshot = new SwitcherSnapshot(switchers[i]);
                if (!snapshot.TryPrepareInactive(out failure))
                    return false;
                validationSwitcherSnapshots.Add(snapshot);
            }
            Light[] validationLights = validationPairRoot.GetComponentsInChildren<Light>(true);
            if (validationLights.Length == 0)
            {
                failure = "Validation-only room pair exposes no duplicate baked-room Lights.";
                return false;
            }
            var seenLights = new HashSet<Light>();
            for (int i = 0; i < validationLights.Length; i++)
            {
                Light light = validationLights[i];
                if (light == null || !seenLights.Add(light))
                    continue;
                validationLightSnapshots.Add(new LightSnapshot(light));
                light.enabled = false;
            }
            Collider[] validationColliders =
                validationPairRoot.GetComponentsInChildren<Collider>(true);
            if (validationColliders.Length == 0)
            {
                failure = "Validation-only room pair exposes no duplicate Colliders.";
                return false;
            }
            var seenColliders = new HashSet<Collider>();
            for (int i = 0; i < validationColliders.Length; i++)
            {
                Collider collider = validationColliders[i];
                if (collider == null || !seenColliders.Add(collider))
                    continue;
                validationColliderSnapshots.Add(new ColliderSnapshot(collider));
                collider.enabled = false;
            }
            failure = null;
            return true;
        }

        private bool TryPrepareActualDoorReceivers(Transform actualDoorLeaf, out string failure)
        {
            DungeonDoorProbeRendererGroup[] groups =
                actualDoorLeaf.GetComponentsInChildren<DungeonDoorProbeRendererGroup>(true);
            if (groups.Length == 0)
            {
                failure = "Generated V2 door exposes no split-renderer SH groups.";
                return false;
            }
            for (int i = 0; i < groups.Length; i++)
            {
                DungeonDoorProbeRendererGroup group = groups[i];
                Renderer renderer = group != null ? group.GetComponent<Renderer>() : null;
                if (renderer == null && group != null)
                    renderer = group.GetComponentInChildren<Renderer>(true);
                if (renderer == null)
                {
                    failure = "Generated V2 door SH group " + i + " has no renderer.";
                    return false;
                }
                bool snapshotted = false;
                for (int snapshotIndex = 0;
                     snapshotIndex < generatedRendererSnapshots.Count;
                     snapshotIndex++)
                {
                    if (generatedRendererSnapshots[snapshotIndex].Renderer != renderer)
                        continue;
                    snapshotted = true;
                    break;
                }
                if (!snapshotted)
                {
                    failure = "Generated door renderer is outside both exact target tile roots.";
                    return false;
                }
                generatedDoorShRenderers.Add(renderer);
            }
            DungeonDoorDualSideProbeReceiver[] receivers =
                actualDoorLeaf.GetComponentsInChildren<DungeonDoorDualSideProbeReceiver>(true);
            if (receivers.Length != 1 || receivers[0] == null)
            {
                failure = "Generated V2 door must expose exactly one production dual-side " +
                          "probe receiver; count=" + receivers.Length + ".";
                return false;
            }
            receivers[0].ForceRefresh();
            productionDoorReceiverSnapshots.Add(new BehaviourSnapshot(receivers[0]));
            receivers[0].enabled = false;
            failure = null;
            return true;
        }

        private bool TryPrepareReflectionRebind(
            out ReflectionRebindConfiguration configuration,
            out string failure)
        {
            configuration = default;
            if (!reflectionDriver.TryGetAuthoringConfiguration(
                    out string startStableId,
                    out ReflectionProbe startOwnedProbe,
                    out DungeonPortalRoomReflectionProfile startProfile,
                    out string[] administrativeStableIds,
                    out ReflectionProbe[] administrativeOwnedProbes,
                    out DungeonPortalRoomReflectionProfile[] administrativeProfiles,
                    out failure))
            {
                return false;
            }
            configuration = new ReflectionRebindConfiguration
            {
                StartStableId = startStableId,
                StartOwnedProbe = startOwnedProbe,
                StartProfile = startProfile,
                AdministrativeStableIds = administrativeStableIds,
                AdministrativeOwnedProbes = administrativeOwnedProbes,
                AdministrativeProfiles = administrativeProfiles
            };

            var owned = new HashSet<ReflectionProbe>(configuration.AdministrativeOwnedProbes);
            owned.Add(configuration.StartOwnedProbe);
            ReflectionProbe[] validationProbes =
                validationPairRoot.GetComponentsInChildren<ReflectionProbe>(true);
            var seenValidation = new HashSet<ReflectionProbe>();
            for (int i = 0; i < validationProbes.Length; i++)
            {
                ReflectionProbe probe = validationProbes[i];
                if (probe == null || owned.Contains(probe) || !seenValidation.Add(probe))
                    continue;
                validationProbeSnapshots.Add(new ProbeSnapshot(probe));
                probe.enabled = false;
            }
            if (validationProbeSnapshots.Count !=
                DungeonPortalBakedBasisReflectionDriver.ExpectedSuppressedProductionProbeCount)
            {
                failure = "Validation-only production-probe count is not exactly ten; count=" +
                          validationProbeSnapshots.Count + ".";
                return false;
            }

            var seenGenerated = new HashSet<ReflectionProbe>();
            Tile[] tiles = { generatedStartTile, generatedAdministrativeTile };
            for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
            {
                ReflectionProbe[] probes =
                    tiles[tileIndex].GetComponentsInChildren<ReflectionProbe>(true);
                for (int i = 0; i < probes.Length; i++)
                {
                    ReflectionProbe probe = probes[i];
                    if (probe == null || owned.Contains(probe) || !seenGenerated.Add(probe))
                        continue;
                    generatedProductionProbes.Add(probe);
                }
            }
            if (generatedProductionProbes.Count !=
                DungeonPortalBakedBasisReflectionDriver.ExpectedSuppressedProductionProbeCount)
            {
                failure = "Actual generated production-probe count is not exactly ten; count=" +
                          generatedProductionProbes.Count + ".";
                return false;
            }
            failure = null;
            return true;
        }

        private static bool TryReadPrivateField<T>(object instance, string fieldName, out T value)
        {
            value = default;
            if (instance == null)
                return false;
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || !(field.GetValue(instance) is T typedValue))
                return false;
            value = typedValue;
            return true;
        }

        private static bool TryWritePrivateField<T>(object instance, string fieldName, T value)
        {
            if (instance == null)
                return false;
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(T))
                return false;
            field.SetValue(instance, value);
            return true;
        }

        private static bool IsRetryablePairPlacementFailure(string failure)
        {
            return !string.IsNullOrEmpty(failure) &&
                   (failure.StartsWith("Administrative doorway drift:", StringComparison.Ordinal) ||
                    failure.StartsWith("Generated pair spatial mismatch:", StringComparison.Ordinal));
        }

        private static bool TryValidateGeneratedTile(
            Tile tile,
            TileSet requiredTileSet,
            GameObject requiredPrefab,
            int requiredPathDepth,
            out string failure)
        {
            if (tile == null || tile.Placement == null || !tile.Placement.IsOnMainPath ||
                tile.Placement.PathDepth != requiredPathDepth ||
                tile.Placement.TileSet != requiredTileSet || tile.Prefab != requiredPrefab ||
                !TileSetContainsExactPrefab(requiredTileSet, tile.Prefab))
            {
                failure = "Generated main-path tile identity/TileSet/path-depth contract failed at index " +
                          requiredPathDepth + ".";
                return false;
            }
            failure = null;
            return true;
        }

        private static bool TryValidateGeneratedTileSetMember(
            Tile tile,
            TileSet requiredTileSet,
            int requiredPathDepth,
            out string failure)
        {
            if (tile == null || tile.Prefab == null || tile.Placement == null ||
                !tile.Placement.IsOnMainPath || tile.Placement.PathDepth != requiredPathDepth ||
                tile.Placement.TileSet != requiredTileSet ||
                !TileSetContainsExactPrefab(requiredTileSet, tile.Prefab))
            {
                failure = "Generated main-path goal is not an exact member of the required " +
                          "production TileSet at path depth " + requiredPathDepth + ".";
                return false;
            }
            failure = null;
            return true;
        }

        private static bool TileSetContainsExactPrefab(TileSet tileSet, GameObject prefab)
        {
            if (tileSet == null || prefab == null || tileSet.TileWeights == null ||
                tileSet.TileWeights.Weights == null)
                return false;
            int matches = 0;
            for (int i = 0; i < tileSet.TileWeights.Weights.Count; i++)
            {
                GameObjectChance entry = tileSet.TileWeights.Weights[i];
                if (entry != null && entry.Value == prefab)
                    matches++;
            }
            return matches == 1;
        }

        public static bool TryApplyRigidDoorwayAlignmentForTest(
            Transform pairRoot,
            Transform validationDoorway,
            Transform generatedDoorway,
            out string failure)
        {
            if (pairRoot == null || validationDoorway == null || generatedDoorway == null ||
                validationDoorway != pairRoot && !validationDoorway.IsChildOf(pairRoot))
            {
                failure = "Rigid doorway alignment requires a pair-root-owned validation doorway and generated doorway.";
                return false;
            }
            Vector3 originalScale = pairRoot.lossyScale;
            Quaternion rotationDelta = generatedDoorway.rotation *
                                       Quaternion.Inverse(validationDoorway.rotation);
            pairRoot.rotation = rotationDelta * pairRoot.rotation;
            pairRoot.position += generatedDoorway.position - validationDoorway.position;
            Physics.SyncTransforms();
            if (Vector3.Distance(validationDoorway.position, generatedDoorway.position) >
                    DoorwayPositionTolerance ||
                Quaternion.Angle(validationDoorway.rotation, generatedDoorway.rotation) >
                    DoorwayRotationToleranceDegrees ||
                Vector3.Distance(pairRoot.lossyScale, originalScale) > 0.00001f)
            {
                failure = "Rigid validation-pair transform did not exactly align the Start doorway.";
                return false;
            }
            failure = null;
            return true;
        }

        private bool TryValidateDoorwayAlignment(out string failure)
        {
            if (!TryValidateDoorwayPoseForTest(
                    validationStartDoorway,
                    generatedStartDoorway != null ? generatedStartDoorway.transform : null,
                    out failure))
                return false;
            if (!TryValidateDoorwayPoseForTest(
                    validationAdministrativeDoorway,
                    generatedAdministrativeDoorway != null
                        ? generatedAdministrativeDoorway.transform
                        : null,
                    out failure))
            {
                failure = "Administrative doorway drift: " + failure;
                return false;
            }
            return true;
        }

        public static bool TryValidateDoorwayPoseForTest(
            Transform validationDoorway,
            Transform generatedDoorway,
            out string failure)
        {
            if (validationDoorway == null || generatedDoorway == null ||
                Vector3.Distance(validationDoorway.position, generatedDoorway.position) >
                    DoorwayPositionTolerance ||
                Quaternion.Angle(validationDoorway.rotation, generatedDoorway.rotation) >
                    DoorwayRotationToleranceDegrees)
            {
                failure = "Validation and generated doorway poses are not coincident.";
                return false;
            }
            failure = null;
            return true;
        }

        private bool TryValidateReferenceCameraSpatialContracts(out string failure)
        {
            Camera[] frames = GetReferenceFrames();
            return TryValidatePointInTargetTileForTest(
                       frames[0].transform.position,
                       generatedStartTile.Bounds,
                       BuildNonTargetBounds(generatedAdministrativeTile),
                       out failure) &&
                   TryValidatePointInTargetTileForTest(
                       frames[1].transform.position,
                       generatedAdministrativeTile.Bounds,
                       BuildNonTargetBounds(generatedStartTile),
                       out failure);
        }

        private Bounds[] BuildNonTargetBounds(Tile pairedTile)
        {
            var result = new Bounds[generatedNonTargetTileBounds.Count + 1];
            result[0] = pairedTile.Bounds;
            for (int i = 0; i < generatedNonTargetTileBounds.Count; i++)
                result[i + 1] = generatedNonTargetTileBounds[i];
            return result;
        }

        private bool TryValidateFrameSpatialContract(int frameIndex, out string failure)
        {
            if (frameIndex < 0 || frameIndex > 1 || actualPlayerCamera == null ||
                localPlayerRoot == null || generatedStartTile == null ||
                generatedAdministrativeTile == null)
            {
                failure = "Generated tile/player frame binding is incomplete.";
                return false;
            }
            Tile target = frameIndex == 0 ? generatedStartTile : generatedAdministrativeTile;
            Tile other = frameIndex == 0 ? generatedAdministrativeTile : generatedStartTile;
            Camera reference = GetReferenceFrames()[frameIndex];
            Bounds[] nonTargets = BuildNonTargetBounds(other);
            if (!TryValidatePointInTargetTileForTest(
                    reference.transform.position, target.Bounds, nonTargets, out failure) ||
                !TryValidatePointInTargetTileForTest(
                    actualPlayerCamera.transform.position, target.Bounds, nonTargets, out failure) ||
                !TryValidatePointInTargetTileForTest(
                    localPlayerRoot.position, target.Bounds, nonTargets, out failure))
            {
                failure = "Frame " + frameIndex + " spatial contract failed: " + failure;
                return false;
            }
            return true;
        }

        public static bool TryValidatePointInTargetTileForTest(
            Vector3 point,
            Bounds target,
            Bounds[] nonTargets,
            out string failure)
        {
            Bounds expandedTarget = target;
            expandedTarget.Expand(TileContainmentTolerance * 2f);
            if (!expandedTarget.Contains(point))
            {
                failure = "Point is outside its corresponding generated tile bounds.";
                return false;
            }
            if (nonTargets != null)
            {
                for (int i = 0; i < nonTargets.Length; i++)
                {
                    if (nonTargets[i].Contains(point))
                    {
                        failure = "Point penetrates non-target generated tile bounds at index " + i + ".";
                        return false;
                    }
                }
            }
            failure = null;
            return true;
        }

        private bool TryValidateGeneratedSurfaceContract(
            bool requireReflectionSuppression,
            bool requireInitialMaterialIdentity,
            out string failure)
        {
            if (generatedRendererSnapshots.Count == 0 ||
                generatedBasisBoundRenderers.Count == 0 ||
                validationRendererSnapshots.Count == 0 ||
                validationProbeSnapshots.Count !=
                    DungeonPortalBakedBasisReflectionDriver.ExpectedSuppressedProductionProbeCount ||
                validationLightSnapshots.Count == 0 ||
                validationColliderSnapshots.Count == 0 ||
                generatedProductionProbes.Count !=
                    DungeonPortalBakedBasisReflectionDriver.ExpectedSuppressedProductionProbeCount ||
                validationSwitcherSnapshots.Count != 2 ||
                productionDoorReceiverSnapshots.Count != 1)
            {
                failure = "Generated-surface runtime rebind snapshots are incomplete.";
                return false;
            }
            if (!TryValidateRendererPopulations(out failure))
                return false;
            for (int i = 0; i < validationRendererSnapshots.Count; i++)
            {
                if (!validationRendererSnapshots[i].TryValidateHidden(out failure))
                    return false;
            }
            for (int i = 0; i < generatedRendererSnapshots.Count; i++)
            {
                RendererSnapshot snapshot = generatedRendererSnapshots[i];
                if (!snapshot.TryValidateGenerated(
                        generatedBasisBoundRenderers.Contains(snapshot.Renderer),
                        requireInitialMaterialIdentity,
                        generatedDoorShRenderers.Contains(snapshot.Renderer),
                        out failure))
                {
                    return false;
                }
            }
            for (int i = 0; i < validationProbeSnapshots.Count; i++)
            {
                if (validationProbeSnapshots[i].Probe == null ||
                    validationProbeSnapshots[i].Probe.enabled)
                {
                    failure = "A hidden validation-pair production ReflectionProbe drifted.";
                    return false;
                }
            }
            for (int i = 0; i < validationLightSnapshots.Count; i++)
            {
                if (!validationLightSnapshots[i].TryValidateDisabled(out failure))
                    return false;
            }
            for (int i = 0; i < validationColliderSnapshots.Count; i++)
            {
                if (!validationColliderSnapshots[i].TryValidateDisabled(out failure))
                    return false;
            }
            for (int i = 0; i < validationSwitcherSnapshots.Count; i++)
            {
                if (!validationSwitcherSnapshots[i].TryValidatePrepared(out failure))
                    return false;
            }
            for (int i = 0; i < productionDoorReceiverSnapshots.Count; i++)
            {
                if (!productionDoorReceiverSnapshots[i].TryValidateDisabled(out failure))
                    return false;
            }
            if (requireReflectionSuppression)
            {
                if (!reflectionDriver.IsInitialized)
                {
                    failure = "Reflection driver has not captured the generated production probes.";
                    return false;
                }
                for (int i = 0; i < generatedProductionProbes.Count; i++)
                {
                    ReflectionProbe probe = generatedProductionProbes[i];
                    if (probe == null || probe.enabled)
                    {
                        failure = "An actual generated production ReflectionProbe is not owned " +
                                  "by the DPBB reflection driver.";
                        return false;
                    }
                }
            }
            failure = null;
            return true;
        }

        private bool TryValidateRendererPopulations(out string failure)
        {
            Renderer[] currentValidation =
                validationPairRoot.GetComponentsInChildren<Renderer>(true);
            var validationSet = new HashSet<Renderer>(currentValidation);
            if (validationSet.Count != validationRendererSnapshots.Count)
            {
                failure = "Validation-pair renderer population drifted; duplicate overlay risk.";
                return false;
            }
            for (int i = 0; i < validationRendererSnapshots.Count; i++)
            {
                if (!validationSet.Contains(validationRendererSnapshots[i].Renderer))
                {
                    failure = "A snapshotted validation renderer was replaced.";
                    return false;
                }
            }

            var generatedSet = new HashSet<Renderer>();
            Tile[] tiles = { generatedStartTile, generatedAdministrativeTile };
            for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
            {
                if (tiles[tileIndex] == null)
                {
                    failure = "A generated target tile disappeared.";
                    return false;
                }
                Renderer[] renderers = tiles[tileIndex].GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                        generatedSet.Add(renderers[i]);
                }
            }
            if (generatedSet.Count != generatedRendererSnapshots.Count)
            {
                failure = "Generated target renderer population drifted; additional renderer " +
                          "duplication is forbidden.";
                return false;
            }
            for (int i = 0; i < generatedRendererSnapshots.Count; i++)
            {
                if (!generatedSet.Contains(generatedRendererSnapshots[i].Renderer))
                {
                    failure = "A generated production renderer was replaced.";
                    return false;
                }
            }
            failure = null;
            return true;
        }

        private void RestoreGeneratedPairState()
        {
            if (reflectionDriver != null)
                reflectionDriver.RestoreProbeStatesForEvidence();
            if (doorShDriver != null)
                doorShDriver.TryRestoreOriginal(out _);
            if (connectionAdjacentSnapshotCaptured && connectionDriver != null)
            {
                connectionDriver.TryRestoreOriginal(out _);
                TryWritePrivateField(
                    connectionDriver,
                    "adjacentTransportEnabled",
                    originalConnectionAdjacentEnabled);
                connectionDriver.ClearEvidenceOverride();
            }
            for (int i = productionDoorReceiverSnapshots.Count - 1; i >= 0; i--)
                productionDoorReceiverSnapshots[i].Restore();
            for (int i = validationSwitcherSnapshots.Count - 1; i >= 0; i--)
                validationSwitcherSnapshots[i].Restore();
            for (int i = validationProbeSnapshots.Count - 1; i >= 0; i--)
                validationProbeSnapshots[i].Restore();
            for (int i = validationLightSnapshots.Count - 1; i >= 0; i--)
                validationLightSnapshots[i].Restore();
            for (int i = validationColliderSnapshots.Count - 1; i >= 0; i--)
                validationColliderSnapshots[i].Restore();
            for (int i = validationRendererSnapshots.Count - 1; i >= 0; i--)
                validationRendererSnapshots[i].Restore();
            for (int i = generatedRendererSnapshots.Count - 1; i >= 0; i--)
                generatedRendererSnapshots[i].Restore();
            productionDoorReceiverSnapshots.Clear();
            validationSwitcherSnapshots.Clear();
            validationProbeSnapshots.Clear();
            validationLightSnapshots.Clear();
            validationColliderSnapshots.Clear();
            validationRendererSnapshots.Clear();
            generatedRendererSnapshots.Clear();
            generatedBasisBoundRenderers.Clear();
            generatedDoorShRenderers.Clear();
            generatedProductionProbes.Clear();
            generatedNonTargetTileBounds.Clear();
            actualGeneratedDoorLeaf = null;
            connectionAdjacentSnapshotCaptured = false;
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds result = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                if (!hasBounds) { result = renderer.bounds; hasBounds = true; }
                else result.Encapsulate(renderer.bounds);
            }
            return hasBounds ? result : new Bounds(root.transform.position, Vector3.one);
        }

        private Camera[] GetReferenceFrames()
        {
            return new[] { startToAdministrativeReferenceCamera, administrativeToStartReferenceCamera };
        }

        private static FPSController FindLocalPlayerController()
        {
            FPSController[] candidates = FindObjectsByType<FPSController>(FindObjectsInactive.Exclude);
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i] != null && candidates[i].isOwner) return candidates[i];
            return null;
        }

        private static Volume ResolveAssignedDungeonVolume(FPSController controller)
        {
            FieldInfo field = typeof(FPSController).GetField("dungeonPostProcessVolume",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null ? field.GetValue(controller) as Volume : null;
        }

        private static Light ResolveAssignedDungeonOnlyLight(FPSController controller)
        {
            FieldInfo field = typeof(FPSController).GetField("dungeonOnlyLight",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null ? field.GetValue(controller) as Light : null;
        }

        private bool TryResolveUniqueProductionZoneManager(
            out DungeonZoneManager result,
            out string failure)
        {
            result = null;
            DungeonZoneManager[] managers =
                FindObjectsByType<DungeonZoneManager>(FindObjectsInactive.Include);
            int productionCount = 0;
            for (int i = 0; i < managers.Length; i++)
            {
                DungeonZoneManager candidate = managers[i];
                if (candidate == null ||
                    !string.Equals(candidate.gameObject.scene.path, StartMapScenePath,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                result = candidate;
                productionCount++;
            }
            if (managers.Length != 1 || productionCount != 1 || result == null ||
                !result.enabled || !result.gameObject.activeInHierarchy)
            {
                failure = "expected exactly one active DungeonZoneManager and it must belong to " +
                          "the production StartMap; total=" + managers.Length +
                          " startMap=" + productionCount;
                result = null;
                return false;
            }
            failure = null;
            return true;
        }

        /// <summary>
        /// Fail-closed production indoor gate used immediately before READY and every capture.
        /// The private fog fields are read from the production component rather than duplicated
        /// as a validation-scene baseline.
        /// </summary>
        public static bool TryValidateProductionIndoorRenderContract(
            DungeonZoneManager manager,
            out string failure)
        {
            if (manager == null ||
                !string.Equals(manager.gameObject.scene.path, StartMapScenePath,
                    StringComparison.Ordinal) ||
                !manager.enabled || !manager.gameObject.activeInHierarchy)
            {
                failure = "The active production StartMap DungeonZoneManager is required.";
                return false;
            }
            if (!TryReadPrivateBool(manager, "controlFogOnDungeonTransition", out bool controlFog) ||
                !TryReadPrivateBool(manager, "fogEnabledInsideDungeon", out bool fogInside))
            {
                failure = "Could not read the production DungeonZoneManager fog contract.";
                return false;
            }
            return TryValidateIndoorRenderContractValuesForTest(
                manager.IsInDungeon,
                manager.overrideIndoorAmbient,
                manager.indoorAmbientMode,
                manager.indoorAmbientLight,
                manager.indoorAmbientIntensity,
                controlFog,
                fogInside,
                RenderSettings.ambientMode,
                RenderSettings.ambientLight,
                RenderSettings.ambientIntensity,
                RenderSettings.fog,
                out failure);
        }

        public static bool TryValidateIndoorRenderContractValuesForTest(
            bool isInDungeon,
            bool overrideIndoorAmbient,
            AmbientMode managerAmbientMode,
            Color managerAmbientLight,
            float managerAmbientIntensity,
            bool controlFogOnDungeonTransition,
            bool fogEnabledInsideDungeon,
            AmbientMode currentAmbientMode,
            Color currentAmbientLight,
            float currentAmbientIntensity,
            bool currentFogEnabled,
            out string failure)
        {
            Color requiredBlack = new Color(0f, 0f, 0f, 1f);
            if (!isInDungeon || !overrideIndoorAmbient ||
                managerAmbientMode != AmbientMode.Flat ||
                !managerAmbientLight.Equals(requiredBlack) ||
                !managerAmbientIntensity.Equals(1f) ||
                !controlFogOnDungeonTransition || fogEnabledInsideDungeon)
            {
                failure = "Production DungeonZoneManager indoor contract must be IsInDungeon, " +
                          "override enabled, Flat black intensity 1, and controlled fog OFF.";
                return false;
            }
            if (currentAmbientMode != managerAmbientMode ||
                !currentAmbientLight.Equals(managerAmbientLight) ||
                !currentAmbientIntensity.Equals(managerAmbientIntensity) ||
                currentFogEnabled != fogEnabledInsideDungeon)
            {
                failure = "Current RenderSettings do not exactly match the production " +
                          "DungeonZoneManager indoor contract.";
                return false;
            }
            failure = null;
            return true;
        }

        private static bool TryReadPrivateBool(
            DungeonZoneManager manager,
            string fieldName,
            out bool value)
        {
            FieldInfo field = typeof(DungeonZoneManager).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(bool))
            {
                value = false;
                return false;
            }
            value = (bool)field.GetValue(manager);
            return true;
        }

        private static bool IsError(string value)
        {
            return string.IsNullOrEmpty(value) || value.StartsWith("ERROR", StringComparison.Ordinal);
        }

        private void Fail(string reason)
        {
            failureReason = string.IsNullOrWhiteSpace(reason) ? "Unknown harness failure." : reason;
            status = "FAILED: " + failureReason;
            if (validationPairRoot != null) validationPairRoot.SetActive(false);
            RestoreGeneratedPairState();
            RestoreOriginalDungeonOnlyLightState();
            Debug.LogError("[DPBB StartMap Harness] " + status, this);
        }

        private bool TryCaptureSpotPolicyFingerprints(out string failure)
        {
            if (!TryValidateProductionIndoorRenderContract(zoneManager, out failure))
                return false;
            DungeonPortalBakedBasisEvidenceSpotPolicy requested = evidenceSpotPolicy;
            if (!TryApplyEvidenceSpotPolicy(
                    DungeonPortalBakedBasisEvidenceSpotPolicy.ORACLE_SPOT_OFF,
                    out failure) ||
                !DungeonPortalBakedBasisRuntimeEnvironmentFingerprint.TryCapture(
                    actualPlayerCamera,
                    assignedDungeonVolume,
                    validationPairRoot,
                    GetReferenceFrames(),
                    DungeonPortalBakedBasisEvidenceSpotPolicy.ORACLE_SPOT_OFF,
                    out oracleSpotOffFingerprint,
                    out failure) ||
                !TryApplyEvidenceSpotPolicy(
                    DungeonPortalBakedBasisEvidenceSpotPolicy.HUMAN_SPOT_ON,
                    out failure) ||
                !DungeonPortalBakedBasisRuntimeEnvironmentFingerprint.TryCapture(
                    actualPlayerCamera,
                    assignedDungeonVolume,
                    validationPairRoot,
                    GetReferenceFrames(),
                    DungeonPortalBakedBasisEvidenceSpotPolicy.HUMAN_SPOT_ON,
                    out humanSpotOnFingerprint,
                    out failure) ||
                !TryApplyEvidenceSpotPolicy(requested, out failure))
            {
                return false;
            }
            environmentFingerprint = ResolveEnvironmentFingerprint(requested);
            failure = null;
            return true;
        }

        private bool TryApplyEvidenceSpotPolicy(
            DungeonPortalBakedBasisEvidenceSpotPolicy policy,
            out string failure)
        {
            if (assignedDungeonOnlyLight == null || !dungeonOnlyLightSnapshotCaptured)
            {
                failure = "Assigned FPSController.dungeonOnlyLight is missing; no fallback light is permitted.";
                return false;
            }
            bool shouldBeEnabled = policy == DungeonPortalBakedBasisEvidenceSpotPolicy.HUMAN_SPOT_ON;
            assignedDungeonOnlyLight.enabled = shouldBeEnabled;
            if (assignedDungeonOnlyLight.enabled != shouldBeEnabled)
            {
                failure = "Assigned FPSController.dungeonOnlyLight did not accept the requested evidence policy.";
                return false;
            }
            evidenceSpotPolicy = policy;
            environmentFingerprint = ResolveEnvironmentFingerprint(policy);
            failure = null;
            return true;
        }

        private DungeonPortalBakedBasisRuntimeEnvironmentFingerprint ResolveEnvironmentFingerprint(
            DungeonPortalBakedBasisEvidenceSpotPolicy policy)
        {
            return policy == DungeonPortalBakedBasisEvidenceSpotPolicy.HUMAN_SPOT_ON
                ? humanSpotOnFingerprint
                : oracleSpotOffFingerprint;
        }

        private void RestoreOriginalDungeonOnlyLightState()
        {
            if (dungeonOnlyLightSnapshotCaptured && assignedDungeonOnlyLight != null)
                assignedDungeonOnlyLight.enabled = originalDungeonOnlyLightEnabled;
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                if (validationPairRoot != null)
                    validationPairRoot.SetActive(false);
                RestoreGeneratedPairState();
                RestoreOriginalDungeonOnlyLightState();
            }
        }

        private void OnDestroy()
        {
            if (Application.isPlaying)
            {
                if (validationPairRoot != null)
                    validationPairRoot.SetActive(false);
                RestoreGeneratedPairState();
                RestoreOriginalDungeonOnlyLightState();
            }
        }

        private sealed class RendererSnapshot
        {
            private readonly Renderer renderer;
            private readonly bool forceRenderingOff;
            private readonly bool enabled;
            private readonly Material[] materials;
            private readonly Shader[] shaders;
            private readonly int[] renderQueues;
            private readonly string[][] shaderKeywords;
            private readonly HashSet<Material>[] allowedMaterials;
            private readonly Mesh mesh;
            private readonly Mesh additionalVertexStreams;
            private readonly ShadowCastingMode shadowCastingMode;
            private readonly bool receiveShadows;
            private readonly uint renderingLayerMask;
            private readonly int gameObjectLayer;
            private readonly LightProbeUsage lightProbeUsage;
            private readonly ReflectionProbeUsage reflectionProbeUsage;
            private readonly Transform probeAnchor;
            private readonly GameObject lightProbeProxyVolumeOverride;
            private readonly MotionVectorGenerationMode motionVectorGenerationMode;
            private readonly bool allowOcclusionWhenDynamic;
            private readonly int rendererPriority;
            private readonly int sortingLayerId;
            private readonly int sortingOrder;
            private readonly bool isPartOfStaticBatch;
            private readonly bool hadPropertyBlock;
            private readonly int lightmapIndex;
            private readonly Vector4 lightmapScaleOffset;
            private readonly int realtimeLightmapIndex;
            private readonly Vector4 realtimeLightmapScaleOffset;

            internal Renderer Renderer => renderer;

            internal RendererSnapshot(Renderer target)
            {
                renderer = target;
                forceRenderingOff = target.forceRenderingOff;
                enabled = target.enabled;
                materials = target.sharedMaterials;
                shaders = new Shader[materials.Length];
                renderQueues = new int[materials.Length];
                shaderKeywords = new string[materials.Length][];
                allowedMaterials = new HashSet<Material>[materials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    shaders[i] = materials[i] != null ? materials[i].shader : null;
                    renderQueues[i] = materials[i] != null ? materials[i].renderQueue : -1;
                    shaderKeywords[i] = GetSortedKeywords(materials[i]);
                    allowedMaterials[i] = new HashSet<Material>();
                    if (materials[i] != null)
                        allowedMaterials[i].Add(materials[i]);
                }
                mesh = ResolveMesh(target);
                additionalVertexStreams = target is MeshRenderer meshRenderer
                    ? meshRenderer.additionalVertexStreams
                    : null;
                shadowCastingMode = target.shadowCastingMode;
                receiveShadows = target.receiveShadows;
                renderingLayerMask = target.renderingLayerMask;
                gameObjectLayer = target.gameObject.layer;
                lightProbeUsage = target.lightProbeUsage;
                reflectionProbeUsage = target.reflectionProbeUsage;
                probeAnchor = target.probeAnchor;
                lightProbeProxyVolumeOverride = target.lightProbeProxyVolumeOverride;
                motionVectorGenerationMode = target.motionVectorGenerationMode;
                allowOcclusionWhenDynamic = target.allowOcclusionWhenDynamic;
                rendererPriority = target.rendererPriority;
                sortingLayerId = target.sortingLayerID;
                sortingOrder = target.sortingOrder;
                isPartOfStaticBatch = target.isPartOfStaticBatch;
                hadPropertyBlock = target.HasPropertyBlock();
                lightmapIndex = target.lightmapIndex;
                lightmapScaleOffset = target.lightmapScaleOffset;
                realtimeLightmapIndex = target.realtimeLightmapIndex;
                realtimeLightmapScaleOffset = target.realtimeLightmapScaleOffset;
            }

            internal void HideForValidation()
            {
                if (renderer != null)
                    renderer.forceRenderingOff = true;
            }

            internal void AddAllowedMaterial(int materialIndex, Material material)
            {
                if (materialIndex >= 0 && materialIndex < allowedMaterials.Length &&
                    material != null)
                {
                    allowedMaterials[materialIndex].Add(material);
                }
            }

            internal bool TryValidateHidden(out string failure)
            {
                if (renderer == null || !renderer.forceRenderingOff || renderer.enabled != enabled ||
                    ResolveMesh(renderer) != mesh ||
                    !TryValidateStructuralState(false) ||
                    renderer.lightmapIndex != lightmapIndex ||
                    renderer.lightmapScaleOffset != lightmapScaleOffset ||
                    renderer.realtimeLightmapIndex != realtimeLightmapIndex ||
                    renderer.realtimeLightmapScaleOffset != realtimeLightmapScaleOffset)
                {
                    failure = "Hidden validation renderer changed mesh or renderer/lightmap state.";
                    return false;
                }
                return TryValidateMaterials(true, out failure);
            }

            internal bool TryValidateGenerated(
                bool allowBakedLightmapMutation,
                bool requireInitialMaterialIdentity,
                bool allowDoorShMutation,
                out string failure)
            {
                if (renderer == null || renderer.forceRenderingOff != forceRenderingOff ||
                    renderer.enabled != enabled || ResolveMesh(renderer) != mesh ||
                    !TryValidateStructuralState(allowDoorShMutation) ||
                    renderer.realtimeLightmapIndex != realtimeLightmapIndex ||
                    renderer.realtimeLightmapScaleOffset != realtimeLightmapScaleOffset ||
                    (!allowBakedLightmapMutation &&
                     (renderer.lightmapIndex != lightmapIndex ||
                      renderer.lightmapScaleOffset != lightmapScaleOffset)))
                {
                    failure = allowBakedLightmapMutation
                        ? "Basis-bound generated renderer changed production mesh/realtime state."
                        : "Unbound generated renderer was mutated by the DPBB runtime.";
                    return false;
                }
                return TryValidateMaterials(requireInitialMaterialIdentity, out failure);
            }

            private bool TryValidateStructuralState(bool allowDoorShMutation)
            {
                Mesh currentAdditionalVertexStreams = renderer is MeshRenderer meshRenderer
                    ? meshRenderer.additionalVertexStreams
                    : null;
                return currentAdditionalVertexStreams == additionalVertexStreams &&
                       renderer.shadowCastingMode == shadowCastingMode &&
                       renderer.receiveShadows == receiveShadows &&
                       renderer.renderingLayerMask == renderingLayerMask &&
                       renderer.gameObject.layer == gameObjectLayer &&
                       (allowDoorShMutation || renderer.lightProbeUsage == lightProbeUsage) &&
                       renderer.reflectionProbeUsage == reflectionProbeUsage &&
                       renderer.probeAnchor == probeAnchor &&
                       renderer.lightProbeProxyVolumeOverride == lightProbeProxyVolumeOverride &&
                       renderer.motionVectorGenerationMode == motionVectorGenerationMode &&
                       renderer.allowOcclusionWhenDynamic == allowOcclusionWhenDynamic &&
                       renderer.rendererPriority == rendererPriority &&
                       renderer.sortingLayerID == sortingLayerId &&
                       renderer.sortingOrder == sortingOrder &&
                       renderer.isPartOfStaticBatch == isPartOfStaticBatch &&
                       (allowDoorShMutation || renderer.HasPropertyBlock() == hadPropertyBlock);
            }

            private bool TryValidateMaterials(bool requireInitialMaterialIdentity, out string failure)
            {
                Material[] current = renderer.sharedMaterials;
                if (current.Length != materials.Length)
                {
                    failure = "Renderer material-array length drifted.";
                    return false;
                }
                for (int i = 0; i < current.Length; i++)
                {
                    Shader shader = current[i] != null ? current[i].shader : null;
                    bool exactOriginal = current[i] == materials[i] && shader == shaders[i] &&
                                         (current[i] == null ||
                                          current[i].renderQueue == renderQueues[i]) &&
                                         KeywordsEqual(
                                             GetSortedKeywords(current[i]), shaderKeywords[i]);
                    bool exactAuthoredVariant = current[i] != null &&
                                                allowedMaterials[i].Contains(current[i]);
                    if ((requireInitialMaterialIdentity && !exactOriginal) ||
                        (!requireInitialMaterialIdentity && !exactOriginal &&
                         !exactAuthoredVariant))
                    {
                        failure = "Renderer material/shader/keyword/render-queue contract drifted.";
                        return false;
                    }
                }
                failure = null;
                return true;
            }

            private static Mesh ResolveMesh(Renderer target)
            {
                if (target is SkinnedMeshRenderer skinned)
                    return skinned.sharedMesh;
                MeshFilter filter = target != null ? target.GetComponent<MeshFilter>() : null;
                return filter != null ? filter.sharedMesh : null;
            }

            private static string[] GetSortedKeywords(Material material)
            {
                if (material == null || material.shaderKeywords == null)
                    return Array.Empty<string>();
                string[] result = (string[])material.shaderKeywords.Clone();
                Array.Sort(result, StringComparer.Ordinal);
                return result;
            }

            private static bool KeywordsEqual(string[] left, string[] right)
            {
                if (left.Length != right.Length)
                    return false;
                for (int i = 0; i < left.Length; i++)
                {
                    if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                        return false;
                }
                return true;
            }

            internal void Restore()
            {
                if (renderer != null)
                    renderer.forceRenderingOff = forceRenderingOff;
            }
        }

        private sealed class SwitcherSnapshot
        {
            private readonly DungeonTileLightmapSwitcher switcher;
            private readonly bool enabled;
            private readonly FieldInfo applyOnAwakeField;
            private readonly bool applyOnAwake;

            internal SwitcherSnapshot(DungeonTileLightmapSwitcher target)
            {
                switcher = target;
                enabled = target != null && target.enabled;
                applyOnAwakeField = typeof(DungeonTileLightmapSwitcher).GetField(
                    "applyOnAwake",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                applyOnAwake = target != null && applyOnAwakeField != null &&
                               (bool)applyOnAwakeField.GetValue(target);
            }

            internal bool TryPrepareInactive(out string failure)
            {
                if (switcher == null || applyOnAwakeField == null ||
                    applyOnAwakeField.FieldType != typeof(bool))
                {
                    failure = "Could not snapshot the validation-only switcher's applyOnAwake field.";
                    return false;
                }
                applyOnAwakeField.SetValue(switcher, false);
                switcher.enabled = false;
                return TryValidatePrepared(out failure);
            }

            internal bool TryValidatePrepared(out string failure)
            {
                if (switcher == null || switcher.enabled || applyOnAwakeField == null ||
                    (bool)applyOnAwakeField.GetValue(switcher))
                {
                    failure = "A validation-only legacy lightmap switcher became active.";
                    return false;
                }
                failure = null;
                return true;
            }

            internal void Restore()
            {
                if (switcher == null || applyOnAwakeField == null)
                    return;
                applyOnAwakeField.SetValue(switcher, applyOnAwake);
                switcher.enabled = enabled;
            }
        }

        private readonly struct BehaviourSnapshot
        {
            private readonly Behaviour behaviour;
            private readonly bool enabled;

            internal BehaviourSnapshot(Behaviour target)
            {
                behaviour = target;
                enabled = target != null && target.enabled;
            }

            internal bool TryValidateDisabled(out string failure)
            {
                if (behaviour == null || behaviour.enabled)
                {
                    failure = "The production door's competing probe receiver is enabled.";
                    return false;
                }
                failure = null;
                return true;
            }

            internal void Restore()
            {
                if (behaviour != null)
                {
                    behaviour.enabled = enabled;
                    if (enabled && behaviour is DungeonDoorDualSideProbeReceiver receiver)
                        receiver.ForceRefresh();
                }
            }
        }

        private struct ReflectionRebindConfiguration
        {
            internal string StartStableId;
            internal ReflectionProbe StartOwnedProbe;
            internal DungeonPortalRoomReflectionProfile StartProfile;
            internal string[] AdministrativeStableIds;
            internal ReflectionProbe[] AdministrativeOwnedProbes;
            internal DungeonPortalRoomReflectionProfile[] AdministrativeProfiles;
        }

        private readonly struct RenderSettingsSnapshot
        {
            private readonly Material skybox;
            private readonly Light sun;
            private readonly AmbientMode ambientMode;
            private readonly Color ambientLight;
            private readonly Color ambientSkyColor;
            private readonly Color ambientEquatorColor;
            private readonly Color ambientGroundColor;
            private readonly float ambientIntensity;
            private readonly float[] ambientProbe;
            private readonly bool fog;
            private readonly FogMode fogMode;
            private readonly Color fogColor;
            private readonly float fogDensity;
            private readonly float fogStartDistance;
            private readonly float fogEndDistance;
            private readonly DefaultReflectionMode defaultReflectionMode;
            private readonly int defaultReflectionResolution;
            private readonly int reflectionBounces;
            private readonly float reflectionIntensity;
            private readonly Texture customReflection;
            private readonly Color subtractiveShadowColor;
            private readonly float haloStrength;
            private readonly float flareStrength;
            private readonly float flareFadeSpeed;

            private RenderSettingsSnapshot(bool capture)
            {
                skybox = RenderSettings.skybox;
                sun = RenderSettings.sun;
                ambientMode = RenderSettings.ambientMode;
                ambientLight = RenderSettings.ambientLight;
                ambientSkyColor = RenderSettings.ambientSkyColor;
                ambientEquatorColor = RenderSettings.ambientEquatorColor;
                ambientGroundColor = RenderSettings.ambientGroundColor;
                ambientIntensity = RenderSettings.ambientIntensity;
                ambientProbe = new float[27];
                SphericalHarmonicsL2 sh = RenderSettings.ambientProbe;
                int index = 0;
                for (int channel = 0; channel < 3; channel++)
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    ambientProbe[index++] = sh[channel, coefficient];
                fog = RenderSettings.fog;
                fogMode = RenderSettings.fogMode;
                fogColor = RenderSettings.fogColor;
                fogDensity = RenderSettings.fogDensity;
                fogStartDistance = RenderSettings.fogStartDistance;
                fogEndDistance = RenderSettings.fogEndDistance;
                defaultReflectionMode = RenderSettings.defaultReflectionMode;
                defaultReflectionResolution = RenderSettings.defaultReflectionResolution;
                reflectionBounces = RenderSettings.reflectionBounces;
                reflectionIntensity = RenderSettings.reflectionIntensity;
                customReflection = RenderSettings.customReflectionTexture;
                subtractiveShadowColor = RenderSettings.subtractiveShadowColor;
                haloStrength = RenderSettings.haloStrength;
                flareStrength = RenderSettings.flareStrength;
                flareFadeSpeed = RenderSettings.flareFadeSpeed;
            }

            internal static RenderSettingsSnapshot CaptureCurrent()
            {
                return new RenderSettingsSnapshot(true);
            }

            internal bool ExactlyEquals(RenderSettingsSnapshot other)
            {
                if (skybox != other.skybox || sun != other.sun || ambientMode != other.ambientMode ||
                    !ambientLight.Equals(other.ambientLight) ||
                    !ambientSkyColor.Equals(other.ambientSkyColor) ||
                    !ambientEquatorColor.Equals(other.ambientEquatorColor) ||
                    !ambientGroundColor.Equals(other.ambientGroundColor) ||
                    !ambientIntensity.Equals(other.ambientIntensity) || fog != other.fog ||
                    fogMode != other.fogMode || !fogColor.Equals(other.fogColor) ||
                    !fogDensity.Equals(other.fogDensity) ||
                    !fogStartDistance.Equals(other.fogStartDistance) ||
                    !fogEndDistance.Equals(other.fogEndDistance) ||
                    defaultReflectionMode != other.defaultReflectionMode ||
                    defaultReflectionResolution != other.defaultReflectionResolution ||
                    reflectionBounces != other.reflectionBounces ||
                    !reflectionIntensity.Equals(other.reflectionIntensity) ||
                    customReflection != other.customReflection ||
                    !subtractiveShadowColor.Equals(other.subtractiveShadowColor) ||
                    !haloStrength.Equals(other.haloStrength) ||
                    !flareStrength.Equals(other.flareStrength) ||
                    !flareFadeSpeed.Equals(other.flareFadeSpeed) ||
                    ambientProbe == null || other.ambientProbe == null ||
                    ambientProbe.Length != other.ambientProbe.Length)
                {
                    return false;
                }
                for (int i = 0; i < ambientProbe.Length; i++)
                {
                    if (!ambientProbe[i].Equals(other.ambientProbe[i]))
                        return false;
                }
                return true;
            }
        }

        private readonly struct ProbeSnapshot
        {
            internal readonly ReflectionProbe Probe;
            private readonly bool enabled;

            internal ProbeSnapshot(ReflectionProbe probe)
            {
                Probe = probe;
                enabled = probe.enabled;
            }

            internal void Restore()
            {
                if (Probe != null)
                    Probe.enabled = enabled;
            }
        }

        private readonly struct LightSnapshot
        {
            private readonly Light light;
            private readonly bool enabled;

            internal LightSnapshot(Light target)
            {
                light = target;
                enabled = target != null && target.enabled;
            }

            internal bool TryValidateDisabled(out string failure)
            {
                if (light == null || light.enabled)
                {
                    failure = "A duplicate validation-pair baked-room Light is enabled or missing.";
                    return false;
                }
                failure = null;
                return true;
            }

            internal void Restore()
            {
                if (light != null)
                    light.enabled = enabled;
            }
        }

        private readonly struct ColliderSnapshot
        {
            private readonly Collider collider;
            private readonly bool enabled;

            internal ColliderSnapshot(Collider target)
            {
                collider = target;
                enabled = target != null && target.enabled;
            }

            internal bool TryValidateDisabled(out string failure)
            {
                if (collider == null || collider.enabled)
                {
                    failure = "A duplicate validation-pair Collider is enabled or missing.";
                    return false;
                }
                failure = null;
                return true;
            }

            internal void Restore()
            {
                if (collider != null)
                    collider.enabled = enabled;
            }
        }
    }
}
