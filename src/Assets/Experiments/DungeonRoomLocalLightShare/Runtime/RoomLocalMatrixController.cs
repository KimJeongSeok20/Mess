using System;
using System.Collections;
using DunGen;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonRoomLocalLightShare
{
    [DefaultExecutionOrder(-150)]
    [DisallowMultipleComponent]
    public sealed class RoomLocalMatrixController : MonoBehaviour
    {
        [SerializeField] private RoomLocalMatrixCatalog catalog;
        [SerializeField] private GameObject fallbackDoorPrefab;
        [SerializeField] private Shader composeShader;
        [SerializeField] private Camera previewCamera;
        [SerializeField] private int caseIndex;
        [SerializeField] private float directIntensity = 1f;
        [SerializeField] private float directRange = 3.8f;
        [SerializeField] private DoorRealtimeMode doorRealtimeMode = DoorRealtimeMode.CookieReceive;
        [SerializeField, Min(0.1f)] private float doorAnimateSeconds = 1.25f;

        private GameObject currentPairRoot;
        private RoomLocalConnection connection;
        private RoomLocalDoorProbeDriver doorProbeDriver;
        private RoomLocalMatrixCatalog.PairCase currentCase;
        private RoomLocalMatrixCatalog.RoomEntry currentRoomA;
        private RoomLocalMatrixCatalog.RoomEntry currentRoomB;
        private RoomLocalMatrixCatalog.DoorwayEntry currentDoorwayA;
        private RoomLocalMatrixCatalog.DoorwayEntry currentDoorwayB;
        private bool rebuildInProgress;
        private int pendingCaseIndex = -1;
        private string status = "Not built";
        private float overlapVolume;
        private float doorAnimateTarget = -1f;

        public RoomLocalMatrixCatalog Catalog => catalog;
        public RoomLocalConnection Connection => connection;
        public RoomLocalDoorProbeDriver DoorProbeDriver => doorProbeDriver;
        public Camera PreviewCamera => previewCamera;
        public int CaseIndex => caseIndex;
        public int CaseCount => catalog != null ? catalog.Cases.Length : 0;
        public string Status => status;
        public float OverlapVolume => overlapVolume;
        public float DirectIntensity => directIntensity;
        public float DirectRange => directRange;
        public DoorRealtimeMode DoorRealtimeMode => doorRealtimeMode;
        public bool IsDoorAnimating => doorAnimateTarget >= 0f;
        public float DoorAnimateTarget => doorAnimateTarget;
        public string CaseLabel => currentCase != null ? currentCase.CaseId : "?";
        public string RoomALabel => FormatEndpoint(currentRoomA, currentDoorwayA);
        public string RoomBLabel => FormatEndpoint(currentRoomB, currentDoorwayB);
        public bool HasCompleteOutgoing =>
            currentDoorwayA != null && currentDoorwayA.Outgoing != null &&
            currentDoorwayB != null && currentDoorwayB.Outgoing != null;
        public bool HasCompleteBounce =>
            currentDoorwayA != null && currentDoorwayA.IncomingBounce != null &&
            currentDoorwayB != null && currentDoorwayB.IncomingBounce != null;

        public void Configure(
            RoomLocalMatrixCatalog authoredCatalog,
            GameObject doorPrefab,
            Shader shader,
            Camera camera)
        {
            catalog = authoredCatalog;
            fallbackDoorPrefab = doorPrefab;
            composeShader = shader;
            previewCamera = camera;
        }

        private void Awake()
        {
            if (previewCamera == null)
                previewCamera = Camera.main;
            BuildCurrentCase();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.LeftBracket))
                PreviousCase();
            if (Input.GetKeyDown(KeyCode.RightBracket))
                NextCase();
            if (Input.GetKeyDown(KeyCode.Alpha1))
                SetConnectionEnabled(false);
            if (Input.GetKeyDown(KeyCode.Alpha2))
                SetConnectionEnabled(true);
            if (Input.GetKeyDown(KeyCode.Alpha3))
                SetPower(DungeonTileLightmapSwitcher.PowerLevel.P100, DungeonTileLightmapSwitcher.PowerLevel.P100);
            if (Input.GetKeyDown(KeyCode.Alpha4))
                SetPower(DungeonTileLightmapSwitcher.PowerLevel.P100, DungeonTileLightmapSwitcher.PowerLevel.P0);
            if (Input.GetKeyDown(KeyCode.Alpha5))
                SetPower(DungeonTileLightmapSwitcher.PowerLevel.P0, DungeonTileLightmapSwitcher.PowerLevel.P100);
            if (Input.GetKeyDown(KeyCode.Alpha6))
                SetPower(DungeonTileLightmapSwitcher.PowerLevel.P0, DungeonTileLightmapSwitcher.PowerLevel.P0);
            if (Input.GetKeyDown(KeyCode.Alpha7))
                SetDoor(0f);
            if (Input.GetKeyDown(KeyCode.Alpha8))
                SetDoor(0.5f);
            if (Input.GetKeyDown(KeyCode.Alpha9))
                SetDoor(1f);
            if (Input.GetKeyDown(KeyCode.O))
                OpenDoor();
            if (Input.GetKeyDown(KeyCode.C))
                CloseDoor();

            AnimateDoorIfNeeded();
        }

        public void PreviousCase()
        {
            RequestCase(caseIndex - 1);
        }

        public void NextCase()
        {
            RequestCase(caseIndex + 1);
        }

        public void JumpToCase(int zeroBasedCaseIndex)
        {
            RequestCase(zeroBasedCaseIndex);
        }

        public void SetConnectionEnabled(bool enabled)
        {
            if (connection != null)
                connection.SetConnectionEnabled(enabled);
        }

        public void SetPower(
            DungeonTileLightmapSwitcher.PowerLevel powerA,
            DungeonTileLightmapSwitcher.PowerLevel powerB)
        {
            if (connection != null)
                connection.SetPowerLevels(powerA, powerB);
        }

        public void SetDoor(float openFraction)
        {
            doorAnimateTarget = -1f;
            ApplyDoor(openFraction);
        }

        public void OpenDoor()
        {
            doorAnimateTarget = 1f;
        }

        public void CloseDoor()
        {
            doorAnimateTarget = 0f;
        }

        private void ApplyDoor(float openFraction)
        {
            if (connection != null && connection.DoorAngle != null)
                connection.DoorAngle.ApplyOpenFraction(openFraction);
        }

        private void AnimateDoorIfNeeded()
        {
            if (doorAnimateTarget < 0f || connection == null || connection.DoorAngle == null)
                return;

            float speed = 1f / Mathf.Max(0.1f, doorAnimateSeconds);
            float current = connection.DoorAngle.OpenFraction;
            float next = Mathf.MoveTowards(current, doorAnimateTarget, speed * Time.deltaTime);
            ApplyDoor(next);
            if (Mathf.Abs(next - doorAnimateTarget) <= 0.0005f)
                doorAnimateTarget = -1f;
        }

        public void SetDirectTuning(float intensity, float range)
        {
            directIntensity = Mathf.Clamp(intensity, 0.25f, 2f);
            directRange = Mathf.Clamp(range, 2.5f, 5f);
            if (connection != null)
                connection.SetRuntimeDirectTuning(directIntensity, directRange);
        }

        public void SetDoorRealtimeMode(DoorRealtimeMode mode)
        {
            doorRealtimeMode = mode;
            if (doorProbeDriver != null)
                doorProbeDriver.SetRealtimeMode(mode);
        }

        public void AimAtDoorwayA()
        {
            AimAt(connection != null ? connection.StartDoorway : null);
        }

        public void AimAtDoorwayB()
        {
            AimAt(connection != null ? connection.AdministrativeDoorway : null);
        }

        private void AimAt(Transform doorway)
        {
            if (previewCamera == null)
                previewCamera = Camera.main;
            AimCamera(doorway);
        }

        private void RequestCase(int requestedIndex)
        {
            if (CaseCount == 0)
                return;
            int wrapped = (requestedIndex % CaseCount + CaseCount) % CaseCount;
            if (rebuildInProgress)
            {
                pendingCaseIndex = wrapped;
                return;
            }
            if (wrapped == caseIndex && currentPairRoot != null)
                return;
            StartCoroutine(RebuildCase(wrapped));
        }

        private IEnumerator RebuildCase(int nextIndex)
        {
            rebuildInProgress = true;
            doorAnimateTarget = -1f;
            status = "Switching matrix case...";
            if (connection != null)
                connection.SetConnectionEnabled(false);
            if (currentPairRoot != null)
            {
#if UNITY_EDITOR
                Transform selected = UnityEditor.Selection.activeTransform;
                if (selected != null &&
                    (selected == currentPairRoot.transform ||
                     selected.IsChildOf(currentPairRoot.transform)))
                    UnityEditor.Selection.activeGameObject = gameObject;
#endif
                Destroy(currentPairRoot);
            }
            currentPairRoot = null;
            connection = null;
            doorProbeDriver = null;
            yield return null;
            caseIndex = nextIndex;
            BuildCurrentCase();
            rebuildInProgress = false;
            if (pendingCaseIndex >= 0)
            {
                int pending = pendingCaseIndex;
                pendingCaseIndex = -1;
                RequestCase(pending);
            }
        }

        private void BuildCurrentCase()
        {
            if (catalog == null || catalog.Cases.Length == 0)
            {
                status = "FAIL: matrix catalog is missing or empty.";
                return;
            }

            caseIndex = Mathf.Clamp(caseIndex, 0, catalog.Cases.Length - 1);
            if (!catalog.TryResolve(
                    caseIndex,
                    out currentCase,
                    out currentRoomA,
                    out currentDoorwayA,
                    out currentRoomB,
                    out currentDoorwayB,
                    out string failure))
            {
                status = "FAIL: " + failure;
                return;
            }

            currentPairRoot = new GameObject("MatrixPair_" + caseIndex.ToString("D3"));
            currentPairRoot.transform.SetParent(transform, false);
            currentPairRoot.SetActive(false);

            GameObject roomA = Instantiate(currentRoomA.Prefab, currentPairRoot.transform);
            GameObject roomB = Instantiate(currentRoomB.Prefab, currentPairRoot.transform);
            roomA.name = "Matrix_A_" + currentRoomA.RoomId;
            roomB.name = "Matrix_B_" + currentRoomB.RoomId;
            ClearBatchingStaticForMatrixInstance(roomA);
            ClearBatchingStaticForMatrixInstance(roomB);
            roomA.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            roomB.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            Transform doorwayA = roomA.transform.Find(currentDoorwayA.Path);
            Transform doorwayB = roomB.transform.Find(currentDoorwayB.Path);
            if (doorwayA == null || doorwayB == null)
            {
                status = "FAIL: selected doorway transform is missing.";
                currentPairRoot.SetActive(true);
                return;
            }

            AlignRootToDoorway(roomB.transform, doorwayB, doorwayA);
            OpenPassage(doorwayA);
            OpenPassage(doorwayB);
            ApplyDungeonRenderingLayer(roomA);
            ApplyDungeonRenderingLayer(roomB);

            Doorway ownerA = doorwayA.GetComponent<Doorway>();
            Doorway ownerB = doorwayB.GetComponent<Doorway>();
            DoorSelection selectedDoor = ResolveDoor(ownerA, ownerB);
            if (selectedDoor.Prefab == null)
            {
                status = "FAIL: selected connection has no door prefab.";
                currentPairRoot.SetActive(true);
                return;
            }

            Doorway placementOwner = selectedDoor.Owner != null ? selectedDoor.Owner : ownerA;
            Transform placementParent = placementOwner != null
                ? placementOwner.transform
                : doorwayA;
            GameObject door = Instantiate(selectedDoor.Prefab, placementParent);
            door.name = selectedDoor.Prefab.name;
            door.SetActive(true);
            door.transform.localPosition = placementOwner != null
                ? placementOwner.DoorPrefabPositionOffset
                : Vector3.zero;
            if (placementOwner != null && placementOwner.AvoidRotatingDoorPrefab)
                door.transform.rotation = Quaternion.Euler(placementOwner.DoorPrefabRotationOffset);
            else
                door.transform.localRotation = Quaternion.Euler(
                    placementOwner != null
                        ? placementOwner.DoorPrefabRotationOffset
                        : Vector3.zero);
            door.transform.localScale = Vector3.one;
            ApplyDungeonRenderingLayer(door);

            Transform leaf = door.transform.Find(RoomLocalLightShareContract.DoorLeafPath);
            if (leaf == null)
            {
                status = "FAIL: door leaf '" + RoomLocalLightShareContract.DoorLeafPath + "' is missing.";
                currentPairRoot.SetActive(true);
                return;
            }

            GameObject pairHost = new GameObject("RoomLocalPairConnection");
            pairHost.transform.SetParent(currentPairRoot.transform, false);
            var angle = pairHost.AddComponent<RoomLocalDoorAngleSource>();
            angle.Configure(leaf, leaf.localRotation, Vector3.up, 90f);
            connection = pairHost.AddComponent<RoomLocalConnection>();
            connection.Configure(
                roomA.transform,
                roomB.transform,
                doorwayA,
                doorwayB,
                currentDoorwayA.Outgoing,
                currentDoorwayB.Outgoing,
                currentDoorwayA.IncomingBounce,
                currentDoorwayB.IncomingBounce,
                composeShader,
                angle);

            var productionDoorReceiver = leaf.GetComponent<DungeonDoorDualSideProbeReceiver>();
            if (productionDoorReceiver != null)
                productionDoorReceiver.enabled = false;
            doorProbeDriver = pairHost.AddComponent<RoomLocalDoorProbeDriver>();
            doorProbeDriver.Configure(
                leaf,
                roomA.transform,
                roomB.transform,
                doorwayA,
                doorwayB,
                roomA.GetComponent<DungeonTileLightmapSwitcher>(),
                roomB.GetComponent<DungeonTileLightmapSwitcher>(),
                currentDoorwayA.Outgoing,
                currentDoorwayB.Outgoing);
            doorProbeDriver.SetRealtimeMode(doorRealtimeMode);

            currentPairRoot.SetActive(true);
            FillOpeningToDoor(doorwayA, leaf);
            FillOpeningToDoor(doorwayB, leaf);
            connection.SetRuntimeDirectTuning(directIntensity, directRange);
            connection.SetConnectionEnabled(true);
            connection.SetPowerLevels(
                DungeonTileLightmapSwitcher.PowerLevel.P0,
                DungeonTileLightmapSwitcher.PowerLevel.P100);
            angle.ApplyOpenFraction(0.5f);

            var environment = previewCamera != null
                ? previewCamera.GetComponent<RoomLocalEnvironment>()
                : null;
            if (environment != null)
            {
                environment.Configure(
                    FindFirstObjectByType<DungeonZoneManager>(),
                    previewCamera.GetComponent<Volume>(),
                    roomA.GetComponent<DungeonTileLightmapSwitcher>(),
                    roomB.GetComponent<DungeonTileLightmapSwitcher>());
            }

            AimCamera(doorwayA);
            overlapVolume = CalculateOverlapVolume(roomA, roomB);
            status = HasCompleteOutgoing
                ? "READY - visual verdict is manual"
                : "FAIL: outgoing doorway data is missing";
        }

        private DoorSelection ResolveDoor(Doorway first, Doorway second)
        {
            Doorway chosen = null;
            bool firstHas = HasConnector(first);
            bool secondHas = HasConnector(second);
            if (firstHas && secondHas)
                chosen = first.DoorPrefabPriority >= second.DoorPrefabPriority ? first : second;
            else if (firstHas)
                chosen = first;
            else if (secondHas)
                chosen = second;

            if (chosen != null)
            {
                for (int i = 0; i < chosen.ConnectorPrefabWeights.Count; i++)
                {
                    if (chosen.ConnectorPrefabWeights[i] != null &&
                        chosen.ConnectorPrefabWeights[i].GameObject != null &&
                        chosen.ConnectorPrefabWeights[i].Weight > 0f)
                        return new DoorSelection(
                            chosen.ConnectorPrefabWeights[i].GameObject,
                            chosen);
                }
            }
            return new DoorSelection(fallbackDoorPrefab, first != null ? first : second);
        }

        private readonly struct DoorSelection
        {
            public readonly GameObject Prefab;
            public readonly Doorway Owner;

            public DoorSelection(GameObject prefab, Doorway owner)
            {
                Prefab = prefab;
                Owner = owner;
            }
        }

        private static bool HasConnector(Doorway doorway)
        {
            if (doorway == null || doorway.ConnectorPrefabWeights == null)
                return false;
            for (int i = 0; i < doorway.ConnectorPrefabWeights.Count; i++)
            {
                if (doorway.ConnectorPrefabWeights[i] != null &&
                    doorway.ConnectorPrefabWeights[i].GameObject != null &&
                    doorway.ConnectorPrefabWeights[i].Weight > 0f)
                    return true;
            }
            return false;
        }

        private void AimCamera(Transform doorway)
        {
            if (previewCamera == null || doorway == null)
                return;
            previewCamera.transform.position =
                doorway.position - doorway.forward * 3.2f + doorway.up * 1.62f;
            Vector3 target = doorway.position + doorway.up * 1.1f;
            previewCamera.transform.rotation = Quaternion.LookRotation(
                target - previewCamera.transform.position,
                Vector3.up);
        }

        private static void AlignRootToDoorway(
            Transform movingRoot,
            Transform movingDoorway,
            Transform fixedDoorway)
        {
            Quaternion target = Quaternion.LookRotation(-fixedDoorway.forward, fixedDoorway.up);
            Quaternion delta = target * Quaternion.Inverse(movingDoorway.rotation);
            movingRoot.rotation = delta * movingRoot.rotation;
            movingRoot.position += fixedDoorway.position - movingDoorway.position;
        }

        private static void OpenPassage(Transform doorway)
        {
            Doorway doorwayComponent = doorway != null
                ? doorway.GetComponent<Doorway>()
                : null;
            if (doorwayComponent != null)
            {
                for (int i = 0; i < doorwayComponent.ConnectorSceneObjects.Count; i++)
                {
                    GameObject connectorObject = doorwayComponent.ConnectorSceneObjects[i];
                    if (connectorObject != null)
                        connectorObject.SetActive(true);
                }
                for (int i = 0; i < doorwayComponent.BlockerSceneObjects.Count; i++)
                {
                    GameObject blockerObject = doorwayComponent.BlockerSceneObjects[i];
                    if (blockerObject != null)
                        blockerObject.SetActive(false);
                }
                return;
            }

            Transform anchor = doorway != null ? doorway.parent : null;
            if (anchor == null)
                return;
            for (int i = 0; i < anchor.childCount; i++)
            {
                Transform child = anchor.GetChild(i);
                if (child.name.StartsWith("Blocker_", System.StringComparison.Ordinal))
                    child.gameObject.SetActive(false);
                string normalizedName = child.name.Replace("_", string.Empty);
                if (normalizedName == "NoDoorPlacement" ||
                    normalizedName == "DoorPlacement")
                    child.gameObject.SetActive(true);
            }
        }

        private static void ApplyDungeonRenderingLayer(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].renderingLayerMask = RoomLocalRenderingLayers.EnvironmentMask;
        }

        private void FillOpeningToDoor(Transform doorway, Transform leaf)
        {
            if (doorway == null || leaf == null || currentPairRoot == null)
                return;
            if (!TryMeasureOpening(doorway, out float holeMinX, out float holeMaxX, out float holeMaxY,
                    out Renderer wallRenderer))
                return;

            if (!TryMeasureLocalAabb(leaf, doorway, out Vector3 doorMin, out Vector3 doorMax))
                return;
            float doorMinX = doorMin.x;
            float doorMaxX = doorMax.x;
            float doorMaxY = doorMax.y;
            const float minFill = 0.04f;

            Material material = wallRenderer != null && wallRenderer.sharedMaterial != null
                ? wallRenderer.sharedMaterial
                : null;
            TryAddOpeningFill(
                doorway,
                holeMinX,
                doorMinX,
                0f,
                Mathf.Max(holeMaxY, doorMaxY),
                material,
                minFill,
                "L");
            TryAddOpeningFill(
                doorway,
                doorMaxX,
                holeMaxX,
                0f,
                Mathf.Max(holeMaxY, doorMaxY),
                material,
                minFill,
                "R");
            if (holeMaxY - doorMaxY > minFill)
                TryAddOpeningFill(
                    doorway,
                    doorMinX,
                    doorMaxX,
                    doorMaxY,
                    holeMaxY,
                    material,
                    minFill,
                    "T");
        }

        private static bool TryMeasureOpening(
            Transform doorway,
            out float holeMinX,
            out float holeMaxX,
            out float holeMaxY,
            out Renderer wallRenderer)
        {
            holeMinX = 0f;
            holeMaxX = 0f;
            holeMaxY = 0f;
            wallRenderer = null;
            if (doorway.parent == null)
                return false;

            MeshFilter[] filters = doorway.parent.GetComponentsInChildren<MeshFilter>(true);
            float innerNeg = float.NegativeInfinity;
            float innerPos = float.PositiveInfinity;
            float lintel = float.PositiveInfinity;
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                if (filter == null || !filter.gameObject.activeInHierarchy || filter.sharedMesh == null ||
                    !filter.sharedMesh.isReadable)
                    continue;
                if (filter.name.IndexOf("Door_01", StringComparison.Ordinal) >= 0 ||
                    filter.name.IndexOf("Probe", StringComparison.Ordinal) >= 0)
                    continue;
                if (filter.name.IndexOf("Uncapped", StringComparison.Ordinal) < 0 &&
                    filter.name.IndexOf("Door", StringComparison.Ordinal) < 0)
                    continue;

                Vector3[] vertices = filter.sharedMesh.vertices;
                for (int v = 0; v < vertices.Length; v++)
                {
                    Vector3 local = doorway.InverseTransformPoint(
                        filter.transform.TransformPoint(vertices[v]));
                    if (local.y < 0.05f)
                    {
                        if (local.x < -0.2f && local.x > innerNeg)
                            innerNeg = local.x;
                        if (local.x > 0.2f && local.x < innerPos)
                            innerPos = local.x;
                    }
                    if (Mathf.Abs(local.x) < 0.8f && local.y > 1.8f && local.y < 3.2f &&
                        local.y < lintel)
                        lintel = local.y;
                }
                if (wallRenderer == null)
                    wallRenderer = filter.GetComponent<Renderer>();
            }

            if (float.IsInfinity(innerNeg) || float.IsInfinity(innerPos) || innerPos - innerNeg < 0.5f)
                return false;
            holeMinX = innerNeg;
            holeMaxX = innerPos;
            holeMaxY = !float.IsInfinity(lintel) && lintel > 1f ? lintel : 2.4f;
            return true;
        }

        private static bool TryMeasureLocalAabb(
            Transform root,
            Transform space,
            out Vector3 min,
            out Vector3 max)
        {
            min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            bool any = false;
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                    continue;
                if (filter.name.IndexOf("Probe", StringComparison.Ordinal) >= 0)
                    continue;
                Vector3[] vertices = filter.sharedMesh.vertices;
                for (int v = 0; v < vertices.Length; v++)
                {
                    Vector3 local = space.InverseTransformPoint(
                        filter.transform.TransformPoint(vertices[v]));
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                    any = true;
                }
            }
            return any;
        }

        private void TryAddOpeningFill(
            Transform doorway,
            float minX,
            float maxX,
            float minY,
            float maxY,
            Material material,
            float minSize,
            string suffix)
        {
            float width = maxX - minX;
            float height = maxY - minY;
            if (width < minSize || height < minSize)
                return;

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "OpeningFill_" + doorway.name + "_" + suffix;
            cube.transform.SetParent(currentPairRoot.transform, false);
            Collider collider = cube.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            cube.transform.position = doorway.TransformPoint(
                new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, -0.1f));
            cube.transform.rotation = Quaternion.LookRotation(doorway.forward, doorway.up);
            cube.transform.localScale = new Vector3(width, height, 0.22f);
            MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
            if (material != null)
                renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.renderingLayerMask = RoomLocalLightShareContract.DungeonRenderingLayerMask;
        }

        private static void ClearBatchingStaticForMatrixInstance(GameObject root)
        {
#if UNITY_EDITOR
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject gameObject = transforms[i].gameObject;
                UnityEditor.StaticEditorFlags flags =
                    UnityEditor.GameObjectUtility.GetStaticEditorFlags(gameObject);
                if ((flags & UnityEditor.StaticEditorFlags.BatchingStatic) == 0)
                    continue;
                UnityEditor.GameObjectUtility.SetStaticEditorFlags(
                    gameObject,
                    flags & ~UnityEditor.StaticEditorFlags.BatchingStatic);
            }
#endif
        }

        private static float CalculateOverlapVolume(GameObject first, GameObject second)
        {
            Bounds a = RendererBounds(first);
            Bounds b = RendererBounds(second);
            Vector3 min = Vector3.Max(a.min, b.min);
            Vector3 max = Vector3.Min(a.max, b.max);
            Vector3 overlap = max - min;
            return overlap.x > 0f && overlap.y > 0f && overlap.z > 0f
                ? overlap.x * overlap.y * overlap.z
                : 0f;
        }

        private static Bounds RendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default;
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is ParticleSystemRenderer)
                    continue;
                if (!found)
                {
                    bounds = renderers[i].bounds;
                    found = true;
                }
                else
                    bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        private static string FormatEndpoint(
            RoomLocalMatrixCatalog.RoomEntry room,
            RoomLocalMatrixCatalog.DoorwayEntry doorway)
        {
            if (room == null || doorway == null)
                return "?";
            return room.RoomId + " / " + doorway.Kind + " / " + doorway.Path;
        }
    }
}
