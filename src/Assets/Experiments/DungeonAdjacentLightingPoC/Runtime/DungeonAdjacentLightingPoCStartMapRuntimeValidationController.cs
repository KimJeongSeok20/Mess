using System.Collections;
using System.Reflection;
using Demo.Scripts.Runtime.Character;
using DunGen;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonAdjacentLightingPoC
{
    /// <summary>
    /// Editor-only validation scene runtime driver.  The validation scene owns only
    /// the Start/Admin room pair; all player, camera, Volume, zone, and dungeon
    /// generation state comes from the real StartMap scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DungeonAdjacentLightingPoCStartMapRuntimeValidationController : MonoBehaviour
    {
        [SerializeField] private GameObject validationPairRoot;
        [SerializeField] private Transform startDoorway;
        [SerializeField] private DungeonTileLightmapSwitcher startLighting;
        [SerializeField] private DungeonTileLightmapSwitcher adminLighting;
        [SerializeField] private DungeonAdjacentLightingPoCPreviewController previewController;
        [SerializeField, Min(5f)] private float startupTimeoutSeconds = 45f;
        [SerializeField, Min(2f)] private float pairClearanceFromDungeon = 12f;
        [SerializeField] private bool generateDungeonOnStart = true;
        [SerializeField] private bool disablePlayerAttachedLights = true;

        private string status = "WAITING TO START";
        private string generationResult = "NONE";
        private string entryResult = "NONE";
        private DungeonZoneManager zoneManager;
        private FPSController localPlayerController;
        private Transform playerRoot;
        private Volume playerVolume;
        private int generatedRoomCount;
        private int disabledPlayerLightCount;

        public string Status => status;
        public string GenerationResult => generationResult;
        public string EntryResult => entryResult;
        public int GeneratedRoomCount => generatedRoomCount;
        public int DisabledPlayerLightCount => disabledPlayerLightCount;
        public bool IsReady => status == "READY";
        public bool IsActualDungeonEnvironmentApplied => zoneManager != null && zoneManager.IsInDungeon;
        public string VolumeProfileName =>
            playerVolume != null && playerVolume.sharedProfile != null
                ? playerVolume.sharedProfile.name
                : "MISSING";
        public bool VolumeEnabled => playerVolume != null && playerVolume.enabled;

        public void Configure(
            GameObject pairRoot,
            Transform validationStartDoorway,
            DungeonTileLightmapSwitcher startRoomLighting,
            DungeonTileLightmapSwitcher administrativeRoomLighting,
            DungeonAdjacentLightingPoCPreviewController validationPreviewController)
        {
            validationPairRoot = pairRoot;
            startDoorway = validationStartDoorway;
            startLighting = startRoomLighting;
            adminLighting = administrativeRoomLighting;
            previewController = validationPreviewController;
        }

        private void Awake()
        {
            if (validationPairRoot != null)
                validationPairRoot.SetActive(false);
        }

        private IEnumerator Start()
        {
            yield return RunValidationBootstrap();
        }

        private IEnumerator RunValidationBootstrap()
        {
            status = "WAITING FOR ACTUAL STARTMAP PLAYER/SERVER";
            float deadline = Time.realtimeSinceStartup + startupTimeoutSeconds;
            NetworkDungeonController dungeonController = null;

            while (Time.realtimeSinceStartup < deadline)
            {
                dungeonController = FindFirstObjectByType<NetworkDungeonController>();
                localPlayerController = FindLocalPlayerController();
                playerRoot = localPlayerController != null
                    ? localPlayerController.transform.root
                    : null;
                zoneManager = FindFirstObjectByType<DungeonZoneManager>();
                if (dungeonController != null && dungeonController.isServer &&
                    playerRoot != null && zoneManager != null)
                {
                    break;
                }

                yield return new WaitForSecondsRealtime(0.25f);
            }

            if (dungeonController == null || !dungeonController.isServer ||
                playerRoot == null || zoneManager == null)
            {
                status = "FAILED: STARTMAP PLAYER/SERVER/ZONE NOT READY";
                yield break;
            }

            if (generateDungeonOnStart)
            {
                status = "GENERATING VIA DebugRemoteControl.GenerateDungeon()";
                generationResult = DebugRemoteControl.GenerateDungeon();
                if (generationResult.StartsWith("ERROR", System.StringComparison.Ordinal))
                {
                    status = $"FAILED: {generationResult}";
                    yield break;
                }
            }

            RuntimeDungeon runtimeDungeon = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                runtimeDungeon = FindFirstObjectByType<RuntimeDungeon>();
                Dungeon dungeon = runtimeDungeon != null
                    ? runtimeDungeon.Generator.CurrentDungeon
                    : null;
                if (dungeon != null && dungeon.AllTiles != null &&
                    dungeon.AllTiles.Count > 0 && !runtimeDungeon.Generator.IsGenerating)
                {
                    generatedRoomCount = dungeon.AllTiles.Count;
                    break;
                }

                yield return new WaitForSecondsRealtime(0.25f);
            }

            if (runtimeDungeon == null || generatedRoomCount == 0)
            {
                status = "FAILED: GENERATED DUNGEON DID NOT BECOME READY";
                yield break;
            }

            status = "ENTERING ACTUAL STARTMAP DUNGEON";
            DebugRemoteControl.BeginWaitForDungeonReady(15f);
            while (Time.realtimeSinceStartup < deadline &&
                   DebugRemoteControl.GetDungeonReadyResult() == "PENDING")
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }

            entryResult = DebugRemoteControl.GoToRandomDungeonArea();
            if (entryResult.StartsWith("ERROR", System.StringComparison.Ordinal))
                zoneManager.EnterDungeon(playerRoot);

            PositionValidationPair(runtimeDungeon.Generator.CurrentDungeon.Bounds);
            validationPairRoot.SetActive(true);
            yield return null;

            localPlayerController = FindLocalPlayerController();
            playerRoot = localPlayerController != null
                ? localPlayerController.transform.root
                : null;
            if (playerRoot == null || localPlayerController == null)
            {
                status = "FAILED: LOCAL PLAYER DISAPPEARED AFTER GENERATION";
                yield break;
            }

            TeleportPlayerToValidationPair(playerRoot);
            zoneManager.EnterDungeon(playerRoot);
            // DungeonEntrance.Interact performs these two calls after EnterDungeon.
            // Reproduce that real entry state before disabling only the attached test light.
            localPlayerController.SetDungeonPostProcessing(true);
            localPlayerController.SetDungeonRenderLayerServerRpc(true);
            playerVolume = ResolveDungeonPostProcessVolume(localPlayerController);

            if (disablePlayerAttachedLights)
                disabledPlayerLightCount = DisableDungeonOnlyLight(localPlayerController);

            if (previewController != null)
            {
                previewController.RefreshReceivers();
                previewController.ApplyValidationState(
                    DungeonTileLightmapSwitcher.PowerLevel.P0,
                    DungeonTileLightmapSwitcher.PowerLevel.P100,
                    true);
            }

            status = "READY";
            Debug.Log(
                "[DungeonAdjacentLightmapPoC] ACTUAL STARTMAP VALIDATION READY " +
                $"rooms={generatedRoomCount} zone={zoneManager.IsInDungeon} " +
                $"playerLightsDisabled={disabledPlayerLightCount} " +
                $"volume={VolumeProfileName}/{(VolumeEnabled ? "ON" : "OFF")} " +
                $"state=StartP0/AdminP100/AdjacentON");
        }

        private void PositionValidationPair(Bounds dungeonBounds)
        {
            if (validationPairRoot == null)
                return;

            Bounds pairBounds = CalculateRendererBounds(validationPairRoot);
            Vector3 targetCenter = new Vector3(
                dungeonBounds.max.x + pairClearanceFromDungeon + pairBounds.extents.x,
                pairBounds.center.y,
                dungeonBounds.center.z);
            Vector3 offset = targetCenter - pairBounds.center;
            offset.y = dungeonBounds.min.y - pairBounds.min.y;
            validationPairRoot.transform.position += offset;
        }

        private void TeleportPlayerToValidationPair(Transform targetPlayerRoot)
        {
            if (targetPlayerRoot == null || startDoorway == null)
                return;

            Vector3 forward = Vector3.ProjectOnPlane(startDoorway.forward, startDoorway.up).normalized;
            if (forward.sqrMagnitude < 0.0001f)
                forward = startDoorway.forward;

            Vector3 targetPosition = startDoorway.position - forward * 3f + startDoorway.up * 0.05f;
            CharacterController characterController = targetPlayerRoot.GetComponent<CharacterController>();
            bool controllerWasEnabled = characterController != null && characterController.enabled;
            if (controllerWasEnabled)
                characterController.enabled = false;

            targetPlayerRoot.SetPositionAndRotation(
                targetPosition,
                Quaternion.LookRotation(forward, startDoorway.up));

            if (controllerWasEnabled)
                characterController.enabled = true;
        }

        private static FPSController FindLocalPlayerController()
        {
            FPSController[] controllers = FindObjectsByType<FPSController>(FindObjectsSortMode.None);
            for (int i = 0; i < controllers.Length; i++)
            {
                FPSController controller = controllers[i];
                if (controller != null && controller.isOwner)
                    return controller;
            }

            return null;
        }

        private static Volume ResolveDungeonPostProcessVolume(FPSController controller)
        {
            if (controller == null)
                return null;

            FieldInfo field = typeof(FPSController).GetField(
                "dungeonPostProcessVolume",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Volume assigned = field != null ? field.GetValue(controller) as Volume : null;
            return assigned != null
                ? assigned
                : controller.GetComponentInChildren<Volume>(true);
        }

        private static int DisableDungeonOnlyLight(FPSController controller)
        {
            if (controller == null)
                return 0;

            FieldInfo field = typeof(FPSController).GetField(
                "dungeonOnlyLight",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Light assigned = field != null ? field.GetValue(controller) as Light : null;
            if (assigned == null)
                assigned = controller.GetComponentInChildren<Light>(true);
            if (assigned == null)
                return 0;

            bool wasEnabled = assigned.enabled;
            assigned.enabled = false;
            return wasEnabled ? 1 : 0;
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            Renderer[] renderers = root != null
                ? root.GetComponentsInChildren<Renderer>(true)
                : null;
            bool hasBounds = false;
            Bounds bounds = default;
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null ||
                        renderer.GetComponentInParent<DungeonAdjacentLightmapExtension>(true) != null)
                    {
                        continue;
                    }

                    if (!hasBounds)
                    {
                        bounds = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }
            }

            return hasBounds ? bounds : new Bounds(root.transform.position, Vector3.one);
        }
    }
}
