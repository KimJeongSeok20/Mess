using System.Collections.Generic;
using UnityEngine;
using DunGen;           // DungeonGenerator, RuntimeDungeon, PostProcessPhase
using PurrNet;         // NetworkBehaviour, NetworkIdentity 등
using UnityEngine.Rendering;

public class NetworkDoorPostProcessor : NetworkBehaviour
{
    [Header("DunGen RuntimeDungeon")]
    [SerializeField] private RuntimeDungeon runtimeDungeon;

    [Header("생성된 네트워크 문들을 모아둘 부모 (선택)")]
    [SerializeField] private Transform doorRoot;

    [System.Serializable]
    public class DoorReplacement
    {
        [Tooltip("DoorHolderMarker.typeId와 매칭되는 ID")]
        public int typeId;

        [Tooltip("실제 네트워크 문 프리팹 (NetworkIdentity + NetworkTransform 포함)")]
        public GameObject doorPrefab;
    }

    [Header("홀더 타입별 -> 네트워크 Door 매핑")]
    [SerializeField] private List<DoorReplacement> replacements = new List<DoorReplacement>();
    
    /// <summary>
    /// 이번 던전에서 서버가 스폰한 네트워크 문들 (재생성 시 정리용)
    /// </summary>
private readonly List<GameObject> _spawnedDoors = new();

    [Header("Rendering Layer")]
    [SerializeField] private bool applyDungeonRenderingLayer = true;
    [SerializeField] private string dungeonRenderingLayerName = "Dungeon";
    private uint _dungeonRenderingLayerMask;

    [Header("Diagnostics")]
    [SerializeField] private bool enableStepTimingDiagnostics = true;
    [SerializeField] private bool forceLocalDoorSpawnForValidation;

    public int SpawnedDoorCount => _spawnedDoors.Count;

    public void SetForceLocalDoorSpawnForValidation(bool enabled)
    {
        forceLocalDoorSpawnForValidation = enabled;
    }

    #region PurrNet lifecycle


    private void Start()
    {
        if (runtimeDungeon == null)
            return;

        _dungeonRenderingLayerMask = ResolveRenderingLayerMask(dungeonRenderingLayerName);

        runtimeDungeon.Generator.RegisterPostProcessStep(OnPostProcessDungeon, 0, PostProcessPhase.BeforeBuiltIn);
    }

    private void OnDestroy()
    {
        if (runtimeDungeon == null)
            return;

        // Unregister our custom method
        runtimeDungeon.Generator.UnregisterPostProcessStep(OnPostProcessDungeon);
    }
    #endregion

    /// <summary>
    /// DunGen의 PostProcess 단계에서 호출되는 콜백
    /// </summary>
    private void OnPostProcessDungeon(DunGen.DungeonGenerator generator)
    {
        Debug.Log("[NetworkDoorPostProcessor] PostProcess");

        float totalStart = Time.realtimeSinceStartup;
        float cleanupMs = 0f;
        float scanMs = 0f;
        float spawnMs = 0f;
        int holderCount = 0;
        int destroyedHolderCount = 0;
        int spawnedDoorCount = 0;
        int missingPrefabCount = 0;
        bool canSpawnDoors = isServer || forceLocalDoorSpawnForValidation;

        if (generator == null)
        {
            Debug.LogError("[NetworkDoorPostProcessor] DungeonGenerator 인스턴스가 null 입니다.", this);
            return;
        }

        GameObject root = generator.Root;
        if (root == null)
        {
            Debug.LogError("[NetworkDoorPostProcessor] generator.Root 가 null 입니다.", this);
            return;
        }

        // 서버면 이전에 스폰한 네트워크 문들 정리
        if (canSpawnDoors)
        {
            float cleanupStart = Time.realtimeSinceStartup;
            ClearSpawnedDoors();
            cleanupMs = (Time.realtimeSinceStartup - cleanupStart) * 1000f;
        }

        // 이 던전 안에서 DoorHolderMarker를 전부 찾는다
        float scanStart = Time.realtimeSinceStartup;
        DoorHolderMarker[] holders = root.GetComponentsInChildren<DoorHolderMarker>(true);
        DunGen.Door[] generatedDoors = root.GetComponentsInChildren<DunGen.Door>(true);
        scanMs = (Time.realtimeSinceStartup - scanStart) * 1000f;
        holderCount = holders.Length;

        foreach (var holder in holders)
        {
            if (holder == null)
                continue;

            // 공통: 부모/트랜스폼 정보 백업
            Transform parent = holder.transform.parent;
            Vector3 localPos = holder.transform.localPosition;
            Quaternion localRot = holder.transform.localRotation;
            Vector3 localScale = holder.transform.localScale;
            int typeId = holder.typeId;

            // 공통: 더미 문(door_holder)은 서버/클라 모두에서 제거
            Destroy(holder.gameObject);
            destroyedHolderCount++;

            // 클라이언트는 여기서 끝.
            // 네트워크 Door는 서버에서 스폰되고, 그게 동기화되어 알아서 생김.
            if (!canSpawnDoors)
                continue;

            // --------------------
            // 여기부터는 서버 전용: 진짜 네트워크 문 스폰
            // --------------------
            GameObject prefab = GetDoorPrefab(typeId);
            if (prefab == null)
            {
                Debug.LogWarning($"[NetworkDoorPostProcessor] typeId {typeId} 에 대응되는 문 프리팹이 없습니다.", this);
                missingPrefabCount++;
                continue;
            }

            float spawnStart = Time.realtimeSinceStartup;
            GameObject door = SpawnNetworkDoor(prefab, parent, localPos, localRot, localScale);
            spawnMs += (Time.realtimeSinceStartup - spawnStart) * 1000f;
            if (door != null)
            {
                _spawnedDoors.Add(door);
                ApplyDungeonRenderingLayer(door);
                ConfigureSpawnedDoorLighting(door, generatedDoors);
                spawnedDoorCount++;
            }
        }

        if (enableStepTimingDiagnostics)
        {
            float totalMs = (Time.realtimeSinceStartup - totalStart) * 1000f;
            Debug.Log(
                $"[DungeonGenDiag][DoorPost] totalMs={totalMs:0.0} cleanupMs={cleanupMs:0.0} scanMs={scanMs:0.0} " +
                $"spawnMs={spawnMs:0.0} holders={holderCount} destroyed={destroyedHolderCount} " +
                $"spawned={spawnedDoorCount} missingPrefab={missingPrefabCount} server={isServer} validationSpawn={forceLocalDoorSpawnForValidation}",
                this);
        }

        Debug.Log("[NetworkDoorPostProcessor] PostProcess Done");
    }


    // ----------------- 헬퍼 함수들 -----------------

    /// <summary>
    /// 이번 던전에서 서버가 스폰해 둔 네트워크 문들을 전부 제거
    /// </summary>
    private void ClearSpawnedDoors()
    {
        foreach (var go in _spawnedDoors)
        {
            if (go == null) continue;

            var id = go.GetComponent<NetworkIdentity>();
            if (id != null && id.isSpawned)
            {
                // PurrNet 실제 Despawn 함수 이름에 맞게 수정
                // 예: id.Despawn(); 또는 NetworkManager.InstanceHandler.Despawn(id);
                id.Despawn();
            }
            else
            {
                Destroy(go);
            }
        }

        _spawnedDoors.Clear();
    }

    /// <summary>
    /// typeId에 맞는 네트워크 문 프리팹 찾기
    /// </summary>
    private GameObject GetDoorPrefab(int typeId)
    {
        foreach (var r in replacements)
        {
            if (r != null && r.doorPrefab != null && r.typeId == typeId)
                return r.doorPrefab;
        }
        return null;
    }

    /// <summary>
    /// Resolve the rendering layer mask from the layer name
    /// </summary>
    private uint ResolveRenderingLayerMask(string layerName)
    {
        int idx = RenderingLayerMask.NameToRenderingLayer(layerName);
        if (idx < 0)
        {
            Debug.LogError($"[NetworkDoorPostProcessor] Rendering Layer '{layerName}' not found. (Project Settings > Tags and Layers > Rendering Layers)", this);
            return 0;
        }
        return 1u << idx;
    }

    /// <summary>
    /// Apply the Dungeon rendering layer to all child renderers of the door
    /// </summary>
    private void ApplyDungeonRenderingLayer(GameObject door)
    {
        if (!applyDungeonRenderingLayer || door == null || _dungeonRenderingLayerMask == 0)
            return;

        var renderers = door.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].renderingLayerMask = _dungeonRenderingLayerMask;
        }
    }

    private void ConfigureSpawnedDoorLighting(GameObject spawnedDoor, DunGen.Door[] generatedDoors)
    {
        if (spawnedDoor == null || generatedDoors == null || generatedDoors.Length == 0)
            return;

        DunGen.Door sourceDoor = FindNearestConnectedDoor(generatedDoors, spawnedDoor.transform.position);
        if (sourceDoor == null || sourceDoor.TileA == null || sourceDoor.TileB == null)
            return;

        var spawnedDoorComponents = spawnedDoor.GetComponentsInChildren<DunGen.Door>(true);
        for (int i = 0; i < spawnedDoorComponents.Length; i++)
        {
            DunGen.Door targetDoor = spawnedDoorComponents[i];
            if (targetDoor == null)
                continue;

            targetDoor.Dungeon = sourceDoor.Dungeon;
            targetDoor.DoorwayA = sourceDoor.DoorwayA;
            targetDoor.DoorwayB = sourceDoor.DoorwayB;
            targetDoor.TileA = sourceDoor.TileA;
            targetDoor.TileB = sourceDoor.TileB;

            var genericReceiver = targetDoor.GetComponent<DungeonDynamicProbeReceiver>();
            if (genericReceiver != null)
                genericReceiver.enabled = false;

            var dualReceiver = targetDoor.GetComponent<DungeonDoorDualSideProbeReceiver>();
            if (dualReceiver == null)
                dualReceiver = targetDoor.gameObject.AddComponent<DungeonDoorDualSideProbeReceiver>();

            dualReceiver.Configure(targetDoor, DungeonTileProbeRegistry.Active);
        }
    }

    private static DunGen.Door FindNearestConnectedDoor(DunGen.Door[] generatedDoors, Vector3 worldPosition)
    {
        DunGen.Door best = null;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < generatedDoors.Length; i++)
        {
            DunGen.Door candidate = generatedDoors[i];
            if (candidate == null || candidate.TileA == null || candidate.TileB == null)
                continue;

            float distance = (candidate.transform.position - worldPosition).sqrMagnitude;
            if (distance >= bestDistance)
                continue;

            best = candidate;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>
    /// Door_holder의 부모/로컬 위치/회전을 기준으로 월드 좌표를 계산해서
    /// PurrNet 네트워크 Door를 스폰
    /// </summary>
    private GameObject SpawnNetworkDoor(GameObject prefab,
                                        Transform parent,
                                        Vector3 localPos,
                                        Quaternion localRot,
                                        Vector3 localScale)
    {
        // 1) 로컬→월드 변환
        Vector3 worldPos = parent != null ? parent.TransformPoint(localPos) : localPos;
        Quaternion worldRot = parent != null ? parent.rotation * localRot : localRot;

        GameObject go = Instantiate(prefab, worldPos, worldRot);
        go.transform.localScale = localScale;

        // 2) 서버 전용: PurrNet 네트워크 스폰. 클라이언트는 이 스폰이 복제되어 문을 받는다.
        //    검증용 로컬 스폰(forceLocalDoorSpawnForValidation)은 네트워크에 올리지 않는다.
        NetworkIdentity identity = go.GetComponent<NetworkIdentity>();
        bool networked = isServer && identity != null && !forceLocalDoorSpawnForValidation;
        if (networked)
        {
            if (!identity.isSpawned)
                identity.Spawn(prefab);

            // NetworkTransform은 부모가 NetworkIdentity일 때만 부모를 동기화한다.
            // 일반 Transform 아래에 두면 클라이언트와 계층이 달라지므로 네트워크 문은 루트에 둔다.
            Transform root = doorRoot != null ? doorRoot : parent;
            if (root != null && root.GetComponent<NetworkIdentity>() != null)
                go.transform.SetParent(root, true);
        }
        else
        {
            if (doorRoot != null)
                go.transform.SetParent(doorRoot, true);
            else if (parent != null)
                go.transform.SetParent(parent, true);
        }

        return go;
    }
}
