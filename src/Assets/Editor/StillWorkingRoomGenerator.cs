using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Candidate-based authoring tool for one NewPrison room. It evaluates eight deterministic
/// layouts before materializing only the highest-scoring hard-gate survivor.
/// </summary>
public static class StillWorkingRoomGenerator
{
    private const string MenuPath = "Tools/Dungeon/Room Generator/Generate Best Records Distribution Room";
    private const string ScenePath = "Assets/SceneTemplateAssets/Scenes/AI Room Gen.unity";
    private const string PreviewRootName = "[StillWorkingRoomGeneratorPreview]";
    private const string RoomName = "RecordsDistribution_FromGenerator";
    private const string PrefabPath = "Assets/Prefabs/map_piece/NewPrison/AI_Room_Gen/RecordsDistribution_FromGenerator.prefab";
    private const string ReportPath = "Reports/RoomGeneration/RecordsDistribution_Candidates.json";
    private const string IsometricCapturePath = "Screenshots/RoomGeneration/RecordsDistribution_Isometric.png";
    private const string TopCapturePath = "Screenshots/RoomGeneration/RecordsDistribution_Top.png";
    private const string DoorPath = "Assets/Prefabs/map_piece/NewPrison/SomePicees/Door_LG_A.prefab";
    private const string CeilingPath = "Assets/Prefabs/map_piece/NewPrison/Lighting_Prefabs/Ceiling_Lights_DualSided.prefab";
    private const string AdminPath = "Assets/Prefabs/map_piece/NewPrison/Tile_modified/AdminstrativeSegregation.prefab";
    private const string CafeteriaPath = "Assets/Prefabs/map_piece/NewPrison/Tile_modified/Cafeteria.prefab";

    [Serializable]
    private sealed class CandidateScore
    {
        public int seed;
        public bool issueEast;
        public bool longCounter;
        public bool archiveL;
        public float serviceZOffset;
        public float propJitter;
        public bool hardGatePass;
        public string rejection;
        public int route;
        public int zones;
        public int focal;
        public int clearance;
        public int density;
        public int total;
    }

    [Serializable]
    private sealed class GenerationReport
    {
        public string status;
        public string theme;
        public int candidateCount;
        public int validCandidateCount;
        public int winnerSeed;
        public int winnerScore;
        public string prefab;
        public string scene;
        public string report;
        public string authoringValidation;
        public string sceneValidation;
        public string isometricCapture;
        public string topCapture;
        public string runtimeBoundary;
        public CandidateScore[] candidates;
    }

    private struct Footprint
    {
        public string Name;
        public Rect Rect;
        public bool Blocks;

        public Footprint(string name, float x, float z, float sizeX, float sizeZ, bool blocks = true)
        {
            Name = name;
            Rect = new Rect(x - sizeX * 0.5f, z - sizeZ * 0.5f, sizeX, sizeZ);
            Blocks = blocks;
        }
    }

    [MenuItem(MenuPath)]
    private static void GenerateFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        string result = GenerateBestRecordsDistributionRoomCli();
        Debug.Log("[StillWorkingRoomGenerator] " + result);
    }

    [MenuItem("Tools/Dungeon/Room Generator/Capture Generated Room Isometric")]
    private static void CaptureGeneratedRoomIsometric()
    {
        Debug.Log("[StillWorkingRoomGenerator] " + CaptureGeneratedRoom(false));
    }

    [MenuItem("Tools/Dungeon/Room Generator/Capture Generated Room Top")]
    private static void CaptureGeneratedRoomTop()
    {
        Debug.Log("[StillWorkingRoomGenerator] " + CaptureGeneratedRoom(true));
    }

    [MenuItem("Tools/Dungeon/Room Generator/Validate Generated Room")]
    private static void ValidateGeneratedRoomMenu()
    {
        GameObject room = FindSceneObject(RoomName);
        Debug.Log("[StillWorkingRoomGenerator] " + (room == null ? "BLOCK generated room not found" : ValidateAuthoredRoom(room)), room);
    }

    [MenuItem("Tools/Dungeon/Room Generator/Report Generated Room State")]
    private static void ReportGeneratedRoomStateMenu()
    {
        GameObject room = FindSceneObject(RoomName);
        if (room == null)
        {
            Debug.Log("[StillWorkingRoomGenerator] BLOCK generated room not found");
            return;
        }

        Bounds bounds;
        TryBounds(room, out bounds);
        DunGen.Tile tile = room.GetComponent<DunGen.Tile>();
        int sourceInstanceCount = Resources.FindObjectsOfTypeAll<GameObject>().Count(item =>
            item != null && !EditorUtility.IsPersistent(item) && item.scene == SceneManager.GetActiveScene() &&
            PrefabUtility.GetOutermostPrefabInstanceRoot(item) == item &&
            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(item) == PrefabPath);
        Transform ceils = room.transform.Find("Ceils");
        Component nav = room.GetComponents<Component>().FirstOrDefault(component => component != null && component.GetType().FullName == "Unity.AI.Navigation.NavMeshSurface");
        bool navDataNull = false;
        if (nav != null)
        {
            SerializedProperty data = new SerializedObject(nav).FindProperty("m_NavMeshData");
            navDataNull = data == null || data.objectReferenceValue == null;
        }
        Vector3 prefabRootPosition = Vector3.positiveInfinity;
        Vector3 prefabRootRotation = Vector3.positiveInfinity;
        Vector3 prefabRootScale = Vector3.positiveInfinity;
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            prefabRootPosition = prefabRoot.transform.localPosition;
            prefabRootRotation = prefabRoot.transform.localEulerAngles;
            prefabRootScale = prefabRoot.transform.localScale;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        string state = string.Format(
            "PASS room={0} connected={1} instances={2} sceneDirty={3} prefabRootPos={4} prefabRootRot={5} prefabRootScale={6} previewPos={7} renderBoundsCenter={8} renderBoundsSize={9} tileBoundsCenter={10} tileBoundsSize={11} ceilsActive={12} navDataNull={13} renderers={14} colliders={15} lights={16}",
            room.name,
            PrefabUtility.GetPrefabInstanceStatus(room),
            sourceInstanceCount,
            SceneManager.GetActiveScene().isDirty,
            prefabRootPosition,
            prefabRootRotation,
            prefabRootScale,
            room.transform.localPosition,
            bounds.center,
            bounds.size,
            tile == null ? Vector3.zero : tile.Bounds.center,
            tile == null ? Vector3.zero : tile.Bounds.size,
            ceils != null && ceils.gameObject.activeSelf,
            navDataNull,
            room.GetComponentsInChildren<Renderer>(true).Length,
            room.GetComponentsInChildren<Collider>(true).Length,
            room.GetComponentsInChildren<Light>(true).Length);
        Debug.Log("[StillWorkingRoomGenerator] " + state, room);
    }

    public static string GenerateBestRecordsDistributionRoomCli()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return Failure("Exit Play Mode before room generation.");
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            return Failure("Wait for Unity compilation and asset refresh to finish.");
        if (Lightmapping.isRunning)
            return Failure("Wait for lightmapping to finish.");

        Scene current = EditorSceneManager.GetActiveScene();
        if (current.IsValid() && current.isDirty)
            return Failure("The active scene has unsaved changes. Save or discard them before CLI generation.");

        try
        {
            Scene scene = current.path == ScenePath
                ? current
                : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorSceneManager.SetActiveScene(scene);

            GameObject startRoom = FindSceneObject("StartRoom");
            GameObject corridorRoom = FindSceneObject("CorridorA");
            GameObject crossRoom = FindSceneObject("CrossRoom");
            GameObject officerRoom = FindSceneObject("OfficerRoom");
            if (startRoom == null || corridorRoom == null || crossRoom == null || officerRoom == null)
                return Failure("AI Room Gen requires StartRoom, CorridorA, CrossRoom, and OfficerRoom donors.");

            SourceSet sources;
            string sourceError;
            if (!TryLoadSources(startRoom, corridorRoom, officerRoom, out sources, out sourceError))
                return Failure(sourceError);

            List<CandidateScore> candidates = BuildAndScoreCandidates();
            GameObject previewRoot = GetOrCreateRoot(scene, PreviewRootName);
            foreach (CandidateScore candidate in candidates)
            {
                if (!candidate.hardGatePass) continue;
                GameObject probe = new GameObject("[CandidateProbe_" + candidate.seed + "]");
                probe.transform.SetParent(previewRoot.transform, false);
                string probeContractError;
                if (!TryCopyRootContract(crossRoom, probe, out probeContractError))
                {
                    candidate.hardGatePass = false;
                    candidate.rejection = probeContractError;
                    candidate.total = 0;
                    Object.DestroyImmediate(probe);
                    continue;
                }
                BuildRoom(probe, sources, candidate);
                Physics.SyncTransforms();
                string probeValidation = ValidateAuthoredRoom(probe);
                if (!probeValidation.StartsWith("PASS", StringComparison.Ordinal))
                {
                    candidate.hardGatePass = false;
                    candidate.rejection = probeValidation;
                    candidate.total = 0;
                }
                Object.DestroyImmediate(probe);
            }
            CandidateScore winner = candidates
                .Where(candidate => candidate.hardGatePass)
                .OrderByDescending(candidate => candidate.total)
                .ThenBy(candidate => candidate.seed)
                .FirstOrDefault();
            if (winner == null)
                return Failure("All layout candidates failed hard gates.");

            DestroyNamedChildren(previewRoot.transform, RoomName);

            GameObject room = new GameObject(RoomName);
            room.transform.SetParent(previewRoot.transform, false);
            room.transform.localPosition = Vector3.zero;
            room.transform.localRotation = Quaternion.identity;
            room.transform.localScale = Vector3.one;

            string contractError;
            if (!TryCopyRootContract(crossRoom, room, out contractError))
            {
                Object.DestroyImmediate(room);
                return Failure(contractError);
            }

            BuildRoom(room, sources, winner);
            Physics.SyncTransforms();

            string validation = ValidateAuthoredRoom(room);
            if (!validation.StartsWith("PASS", StringComparison.Ordinal))
            {
                Object.DestroyImmediate(room);
                return Failure(validation);
            }

            DunGen.Tile tile = room.GetComponent<DunGen.Tile>();
            tile.RecalculateBounds();
            EditorUtility.SetDirty(tile);

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? "Assets");
            GameObject connected = PrefabUtility.SaveAsPrefabAssetAndConnect(room, PrefabPath, InteractionMode.AutomatedAction);
            if (connected == null)
            {
                Object.DestroyImmediate(room);
                return Failure("PrefabUtility failed to save the generated room.");
            }

            float previewX = GetPreviewX(scene, room, 6f);
            room.transform.localPosition = new Vector3(previewX, 0f, 0f);
            room.transform.localRotation = Quaternion.identity;
            room.transform.localScale = Vector3.one;

            Selection.activeGameObject = room;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            string sceneValidation = ValidateSceneAndPrefabState(room);
            if (!sceneValidation.StartsWith("PASS", StringComparison.Ordinal))
                return Failure(sceneValidation);

            string isometricCapture = CaptureGeneratedRoom(false);
            if (!isometricCapture.StartsWith("PASS", StringComparison.Ordinal))
                return Failure(isometricCapture);
            string topCapture = CaptureGeneratedRoom(true);
            if (!topCapture.StartsWith("PASS", StringComparison.Ordinal))
                return Failure(topCapture);

            var report = new GenerationReport
            {
                status = "CONDITIONAL",
                theme = "Isolation wing records intake and property distribution",
                candidateCount = candidates.Count,
                validCandidateCount = candidates.Count(candidate => candidate.hardGatePass),
                winnerSeed = winner.seed,
                winnerScore = winner.total,
                prefab = PrefabPath,
                scene = ScenePath,
                report = ReportPath,
                authoringValidation = validation,
                sceneValidation = sceneValidation,
                isometricCapture = isometricCapture,
                topCapture = topCapture,
                runtimeBoundary = "DunGen runtime placement, NavMesh bake, rotated variants, light bake, and door operation remain separate gates.",
                candidates = candidates.ToArray(),
            };

            string json = JsonUtility.ToJson(report, true);
            string absoluteReport = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteReport));
            File.WriteAllText(absoluteReport, json);
            AssetDatabase.Refresh();
            Debug.Log("[StillWorkingRoomGenerator] " + json, room);
            return json;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            return Failure(exception.GetType().Name + ": " + exception.Message);
        }
    }

    private static string ValidateSceneAndPrefabState(GameObject room)
    {
        int instanceCount = Resources.FindObjectsOfTypeAll<GameObject>().Count(item =>
            item != null && !EditorUtility.IsPersistent(item) && item.scene == SceneManager.GetActiveScene() &&
            PrefabUtility.GetOutermostPrefabInstanceRoot(item) == item &&
            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(item) == PrefabPath);
        if (instanceCount != 1) return "BLOCK generated prefab scene instance count=" + instanceCount;
        if (PrefabUtility.GetPrefabInstanceStatus(room) != PrefabInstanceStatus.Connected)
            return "BLOCK generated room is not a connected prefab instance";

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform transform = prefabRoot.transform;
            if (transform.localPosition.sqrMagnitude > 0.000001f || Quaternion.Angle(transform.localRotation, Quaternion.identity) > 0.001f ||
                (transform.localScale - Vector3.one).sqrMagnitude > 0.000001f)
                return "BLOCK prefab root transform is not zero/identity/one";
            DunGen.Tile prefabTile = prefabRoot.GetComponent<DunGen.Tile>();
            if (prefabTile == null || !prefabTile.HasValidBounds)
                return "BLOCK prefab asset has invalid serialized DunGen.Tile bounds";
            Vector3 size = prefabTile.Placement.LocalBounds.size;
            if (size.x < 13.9f || size.x > 14.1f || size.y < 3.4f || size.y > 3.6f || size.z < 13.8f || size.z > 14.1f)
                return "BLOCK prefab asset serialized DunGen.Tile bounds size=" + size;
            if (prefabRoot.GetComponentsInChildren<DunGen.Doorway>(true).Length != 2)
                return "BLOCK prefab asset doorway count is not 2";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
        return "PASS connected=Connected instances=1 prefabRoot=zero/identity/one";
    }

    private sealed class SourceSet
    {
        public GameObject Floor;
        public GameObject Wall;
        public GameObject PillarWest;
        public GameObject PillarEast;
        public GameObject Desk;
        public GameObject ReceptionWall;
        public GameObject Pc;
        public GameObject Shelf;
        public GameObject Bench;
        public GameObject Noticeboard;
        public GameObject BoxA;
        public GameObject BoxB;
        public GameObject GarbageBin;
        public GameObject LongCounter;
        public GameObject ShortCounter;
        public GameObject Door;
        public GameObject Ceiling;
    }

    private static bool TryLoadSources(GameObject start, GameObject corridor, GameObject officer, out SourceSet set, out string error)
    {
        set = new SourceSet();
        error = null;
        GameObject admin = AssetDatabase.LoadAssetAtPath<GameObject>(AdminPath);
        GameObject cafeteria = AssetDatabase.LoadAssetAtPath<GameObject>(CafeteriaPath);

        set.Floor = FindDeep(start.transform.Find("Floors"), "Floor");
        set.Wall = FindDeep(start.transform.Find("Walls"), "Wall_Interior_Uncapped");
        set.PillarWest = FindDeep(start.transform.Find("Pillars"), "Pillar");
        set.PillarEast = FindDeep(start.transform.Find("Pillars"), "Pillar (3)");
        set.Bench = FindDeep(start.transform.Find("Props/No_Interactables"), "Bench_04");
        set.Desk = FindDeep(corridor.transform.Find("Props/No_Interactables"), "Desk_01_CustomColor");
        set.ReceptionWall = FindDeep(corridor.transform.Find("Walls"), "Wall_Interior_Reception");
        set.Pc = FindDeep(corridor.transform.Find("Props/No_Interactables"), "PC_Complete");
        set.Shelf = FindDeep(corridor.transform.Find("Props/No_Interactables"), "Shelf_09_Full");
        set.Noticeboard = FindDeep(officer.transform.Find("Props/No_Interactables"), "Noticeboard");
        set.BoxA = FindDeep(officer.transform.Find("Props/No_Interactables"), "Box_02");
        set.BoxB = FindDeep(officer.transform.Find("Props/No_Interactables"), "Box_02 (1)");
        set.GarbageBin = FindDeep(officer.transform.Find("Props/No_Interactables"), "GarbageBin_01");
        set.ShortCounter = admin == null ? null : FindDeep(admin.transform.Find("Props/No_Interactables"), "Table_03");
        set.LongCounter = cafeteria == null ? null : FindDeep(cafeteria.transform.Find("Props/No_Interactables"), "Table_Foodservice");
        set.Door = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPath);
        set.Ceiling = AssetDatabase.LoadAssetAtPath<GameObject>(CeilingPath);

        var required = new Dictionary<string, GameObject>
        {
            { "Floor", set.Floor }, { "Wall", set.Wall }, { "PillarWest", set.PillarWest },
            { "PillarEast", set.PillarEast }, { "Desk", set.Desk }, { "ReceptionWall", set.ReceptionWall }, { "PC", set.Pc },
            { "Shelf", set.Shelf }, { "Bench", set.Bench }, { "Noticeboard", set.Noticeboard },
            { "BoxA", set.BoxA }, { "BoxB", set.BoxB }, { "LongCounter", set.LongCounter },
            { "ShortCounter", set.ShortCounter }, { "Door", set.Door }, { "Ceiling", set.Ceiling },
        };
        string[] missing = required.Where(pair => pair.Value == null).Select(pair => pair.Key).ToArray();
        if (missing.Length == 0)
            return true;

        error = "Missing required donor objects: " + string.Join(", ", missing);
        return false;
    }

    private static List<CandidateScore> BuildAndScoreCandidates()
    {
        var result = new List<CandidateScore>();
        int seed = 41001;
        for (int side = 0; side < 2; side++)
        for (int counter = 0; counter < 2; counter++)
        for (int archive = 0; archive < 2; archive++)
        {
            bool issueEast = side == 0;
            bool longCounter = counter == 0;
            bool archiveL = archive == 1;
            int candidateSeed = seed++;
            var random = new System.Random(candidateSeed);
            float serviceZOffset = ((candidateSeed % 3) - 1) * 0.18f;
            float propJitter = Mathf.Lerp(-0.16f, 0.16f, (float)random.NextDouble());
            CandidateScore score = ScoreCandidate(candidateSeed, issueEast, longCounter, archiveL, serviceZOffset, propJitter);
            result.Add(score);
        }
        return result;
    }

    private static CandidateScore ScoreCandidate(int seed, bool issueEast, bool longCounter, bool archiveL, float serviceZOffset, float propJitter)
    {
        List<Footprint> footprints = BuildFootprints(issueEast, longCounter, archiveL, serviceZOffset, propJitter);
        var failures = new List<string>();
        Rect centerRoute = Rect.MinMaxRect(5.75f, 1.8f, 8.25f, 12.2f);
        Rect southDoor = Rect.MinMaxRect(5.2f, 0f, 8.8f, 2f);
        Rect northDoor = Rect.MinMaxRect(5.2f, 12f, 8.8f, 14f);
        Rect crossAisle = Rect.MinMaxRect(2f, 7.2f, 12f, 8.6f);

        foreach (Footprint item in footprints.Where(item => item.Blocks))
        {
            if (OverlapsArea(item.Rect, centerRoute, 0.001f)) failures.Add(item.Name + " enters central route");
            if (OverlapsArea(item.Rect, southDoor, 0.001f)) failures.Add(item.Name + " enters south door clearance");
            if (OverlapsArea(item.Rect, northDoor, 0.001f)) failures.Add(item.Name + " enters north door clearance");
            if (OverlapsArea(item.Rect, crossAisle, 0.001f)) failures.Add(item.Name + " enters cross aisle");
            if (item.Rect.xMin < 0f || item.Rect.yMin < 0f || item.Rect.xMax > 14f || item.Rect.yMax > 14f)
                failures.Add(item.Name + " leaves room envelope");
        }

        for (int i = 0; i < footprints.Count; i++)
        for (int j = i + 1; j < footprints.Count; j++)
        {
            if (!footprints[i].Blocks || !footprints[j].Blocks) continue;
            if (OverlapsArea(footprints[i].Rect, footprints[j].Rect, 0.03f))
                failures.Add(footprints[i].Name + " overlaps " + footprints[j].Name);
        }

        int route = 30;
        int zones = 25;
        int focal = 14 + (longCounter ? 4 : 1) + (issueEast ? 2 : 0) - (archiveL ? 1 : 0) - Mathf.RoundToInt(Mathf.Abs(serviceZOffset) * 3f);
        int clearance = 15 - (archiveL ? 3 : 0) - Mathf.RoundToInt(Mathf.Abs(propJitter) * 4f);
        int density = 8 + (longCounter ? 1 : 0) + (archiveL ? 1 : 0);
        bool pass = failures.Count == 0;
        return new CandidateScore
        {
            seed = seed,
            issueEast = issueEast,
            longCounter = longCounter,
            archiveL = archiveL,
            serviceZOffset = serviceZOffset,
            propJitter = propJitter,
            hardGatePass = pass,
            rejection = pass ? "" : string.Join("; ", failures.Distinct().ToArray()),
            route = pass ? route : 0,
            zones = pass ? zones : 0,
            focal = pass ? focal : 0,
            clearance = pass ? clearance : 0,
            density = pass ? density : 0,
            total = pass ? route + zones + focal + clearance + density : 0,
        };
    }

    private static List<Footprint> BuildFootprints(bool issueEast, bool longCounter, bool archiveL, float serviceZOffset, float propJitter)
    {
        float issueX = issueEast ? 10.3f : 3.7f;
        float recordX = 14f - issueX;
        float issueWallX = issueEast ? 13.3f : 0.7f;
        float archiveWallX = 14f - issueWallX;
        var items = new List<Footprint>
        {
            new Footprint("RecordDesk", recordX, 5.2f + serviceZOffset, 1.02f, 1.98f),
            new Footprint("IssueCounter", issueX, 5.75f + serviceZOffset, longCounter ? 1.03f : 1.0f, longCounter ? 2.59f : 2.1f),
            new Footprint("ArchiveShelfA", archiveWallX, 9.25f + propJitter, 0.50f, 1.10f),
            new Footprint("ArchiveShelfB", archiveWallX, 10.95f + propJitter, 0.50f, 1.10f),
            new Footprint("WaitingBench", issueWallX, 10.5f - propJitter, 0.54f, 3.50f),
        };
        if (archiveL)
            items.Add(new Footprint("ArchiveShelfL", issueEast ? 2.35f : 11.65f, 12.55f, 1.10f, 0.50f));
        return items;
    }

    private static void BuildRoom(GameObject room, SourceSet source, CandidateScore winner)
    {
        Transform floors = CreateRoot(room.transform, "Floors");
        Transform walls = CreateRoot(room.transform, "Walls");
        Transform doorways = CreateRoot(room.transform, "Doorways");
        Transform ceils = CreateRoot(room.transform, "Ceils");
        Transform pillars = CreateRoot(room.transform, "Pillars");
        Transform props = CreateRoot(room.transform, "Props");
        CreateRoot(props, "Interactables");
        Transform noInteract = CreateRoot(props, "No_Interactables");
        Transform records = CreateRoot(noInteract, "01_RecordsIntake");
        Transform issue = CreateRoot(noInteract, "02_PropertyDistribution");
        Transform archive = CreateRoot(noInteract, "03_Archive");
        Transform waiting = CreateRoot(noInteract, "04_Waiting");
        Transform details = CreateRoot(noInteract, "05_Details");

        Vector3[] floorsAt = { new Vector3(7f, 0f, 7f), new Vector3(14f, 0f, 7f), new Vector3(7f, 0f, 14f), new Vector3(14f, 0f, 14f) };
        foreach (Vector3 at in floorsAt) Clone(source.Floor, floors, "Floor", at, 0f);
        Vector3[] ceilsAt = { new Vector3(7f, 3.5f, 0f), new Vector3(14f, 3.5f, 0f), new Vector3(7f, 3.5f, 7f), new Vector3(14f, 3.5f, 7f) };
        foreach (Vector3 at in ceilsAt) InstantiatePrefab(source.Ceiling, ceils, "Ceiling_Lights_DualSided", at, 90f);

        foreach (Vector3 at in new[] { new Vector3(3.5f, 0f, 0f), new Vector3(14f, 0f, 0f) }) Clone(source.Wall, walls, "Wall_Interior_Uncapped", at, 0f);
        foreach (Vector3 at in new[] { new Vector3(0f, 0f, 14f), new Vector3(10.5f, 0f, 14f) }) Clone(source.Wall, walls, "Wall_Interior_Uncapped", at, 180f);
        foreach (float z in new[] { 0f, 3.5f, 7f, 10.5f }) Clone(source.Wall, walls, "Wall_Interior_Uncapped", new Vector3(0f, 0f, z), 90f);
        foreach (float z in new[] { 3.5f, 7f, 10.5f, 14f }) Clone(source.Wall, walls, "Wall_Interior_Uncapped", new Vector3(14f, 0f, z), 270f);

        // Two opposed service windows turn the centre into a legible public lane while keeping
        // staff furniture and archives in the side bands. Both stop before the cross aisle.
        Clone(source.ReceptionWall, walls, "RecordsServiceWindow", new Vector3(5.45f, 0f, 5.15f + winner.serviceZOffset), 90f);
        Clone(source.ReceptionWall, walls, "DistributionServiceWindow", new Vector3(8.55f, 0f, 5.15f + winner.serviceZOffset), 270f);

        InstantiatePrefab(source.Door, doorways, "Door_LG_A", new Vector3(7f, 0f, 0f), 180f);
        InstantiatePrefab(source.Door, doorways, "Door_LG_A", new Vector3(7f, 0f, 14f), 0f);
        Clone(source.PillarWest, pillars, "Pillar", new Vector3(0.2f, 0f, 3.16f), 90f);
        Clone(source.PillarWest, pillars, "Pillar", new Vector3(0.2f, 0f, 10.16f), 90f);
        Clone(source.PillarEast, pillars, "Pillar", new Vector3(13.81f, 0f, 3.16f), 270f);
        Clone(source.PillarEast, pillars, "Pillar", new Vector3(13.81f, 0f, 10.16f), 270f);

        float issueX = winner.issueEast ? 10.3f : 3.7f;
        float recordsX = 14f - issueX;
        float issueWallX = winner.issueEast ? 13.3f : 0.7f;
        float archiveWallX = 14f - issueWallX;
        float issueYaw = winner.issueEast ? 270f : 90f;
        float recordsYaw = winner.issueEast ? 90f : 270f;

        GameObject desk = PlaceGrounded(source.Desk, records, "RecordsDesk", recordsX, 5.2f + winner.serviceZOffset, recordsYaw, 0f);
        Bounds deskBounds;
        TryBounds(desk, out deskBounds);
        PlaceGrounded(source.Pc, records, "RecordsTerminal", recordsX, 5.2f + winner.serviceZOffset, recordsYaw, deskBounds.max.y + 0.003f);

        GameObject counterSource = winner.longCounter ? source.LongCounter : source.ShortCounter;
        GameObject counter = PlaceGrounded(counterSource, issue, winner.longCounter ? "LongDistributionCounter" : "CompactDistributionCounter", issueX, 5.75f + winner.serviceZOffset, issueYaw, 0f);
        Bounds counterBounds;
        TryBounds(counter, out counterBounds);
        float boxXOffset = winner.issueEast ? -0.12f : 0.12f;
        PlaceGrounded(source.BoxA, issue, "PreparedPropertyBox_A", issueX + boxXOffset, 5.35f + winner.serviceZOffset, 12f, counterBounds.max.y + 0.006f);
        PlaceGrounded(source.BoxB, issue, "PreparedPropertyBox_B", issueX - boxXOffset, 5.93f + winner.serviceZOffset, 346f, counterBounds.max.y + 0.006f);

        PlaceGrounded(source.Shelf, archive, "ArchiveShelf_A", archiveWallX, 9.25f + winner.propJitter, recordsYaw, 0f);
        PlaceGrounded(source.Shelf, archive, "ArchiveShelf_B", archiveWallX, 10.95f + winner.propJitter, recordsYaw, 0f);
        if (winner.archiveL)
            PlaceGrounded(source.Shelf, archive, "ArchiveShelf_L", winner.issueEast ? 2.35f : 11.65f, 12.55f, 0f, 0f);

        PlaceGrounded(source.Bench, waiting, "ClaimantBench", issueWallX, 10.5f - winner.propJitter, issueYaw, 0f);
        PlaceWall(source.Noticeboard, details, "RecordsNoticeboard", archiveWallX, 5.15f, 1.8f, recordsYaw);
        if (source.GarbageBin != null)
            PlaceGrounded(source.GarbageBin, details, "ServiceWasteBin", winner.issueEast ? 12.7f : 1.3f, 3.1f, 0f, 0f);
    }

    private static string ValidateAuthoredRoom(GameObject room)
    {
        string[] roots = { "Floors", "Walls", "Doorways", "Ceils", "Pillars", "Props", "Props/Interactables", "Props/No_Interactables" };
        string[] missing = roots.Where(path => room.transform.Find(path) == null).ToArray();
        if (missing.Length > 0) return "BLOCK missing hierarchy: " + string.Join(", ", missing);

        string[] requiredTypes = { "Unity.AI.Navigation.NavMeshSurface", "DunGen.Tile", "ApplyRenderingLayer", "PurrNet.NetworkTransform" };
        foreach (string type in requiredTypes)
        {
            int count = room.GetComponents<Component>().Count(component => component != null && component.GetType().FullName == type);
            if (count != 1) return "BLOCK root component " + type + " count=" + count;
        }

        DunGen.Tile tile = room.GetComponent<DunGen.Tile>();
        if (tile == null) return "BLOCK missing DunGen.Tile";
        tile.RecalculateBounds();
        if (!tile.HasValidBounds || tile.Placement.LocalBounds.size.sqrMagnitude <= 0.001f)
            return "BLOCK invalid DunGen.Tile bounds";

        DunGen.Doorway[] doors = room.GetComponentsInChildren<DunGen.Doorway>(true);
        if (doors.Length != 2) return "BLOCK doorway count=" + doors.Length;
        Vector3 center = room.transform.TransformPoint(new Vector3(7f, 0f, 7f));
        foreach (DunGen.Doorway door in doors)
        {
            Bounds doorwayBounds;
            bool axisAligned;
            bool edgePositioned;
            if (!door.ValidateTransform(out doorwayBounds, out axisAligned, out edgePositioned) || !axisAligned || !edgePositioned)
                return "BLOCK DunGen doorway transform invalid " + door.name;
            Vector3 radial = door.transform.position - center;
            radial.y = 0f;
            Vector3 forward = door.transform.forward;
            forward.y = 0f;
            if (radial.sqrMagnitude < 0.01f || Vector3.Dot(radial.normalized, forward.normalized) < 0.98f)
                return "BLOCK inward or invalid doorway " + door.name;
        }

        Transform ceils = room.transform.Find("Ceils");
        Bounds ceilingBounds;
        if (!TryBounds(ceils.gameObject, out ceilingBounds)) return "BLOCK ceiling has no bounds";
        Vector3 localMin = room.transform.InverseTransformPoint(ceilingBounds.min);
        Vector3 localMax = room.transform.InverseTransformPoint(ceilingBounds.max);
        if (localMin.x > 0.05f || localMin.z > 0.05f || localMax.x < 13.95f || localMax.z < 13.95f)
            return "BLOCK incomplete ceiling footprint min=" + localMin + " max=" + localMax;

        if (room.GetComponentsInChildren<Component>(true).Any(component => component == null))
            return "BLOCK missing script component";

        float floorY = room.transform.TransformPoint(Vector3.zero).y;
        string[] groundedNames =
        {
            "RecordsDesk", "LongDistributionCounter", "CompactDistributionCounter",
            "ArchiveShelf_A", "ArchiveShelf_B", "ArchiveShelf_L", "ClaimantBench", "ServiceWasteBin",
        };
        foreach (string groundedName in groundedNames)
        {
            GameObject grounded = FindDeep(room.transform, groundedName);
            Bounds groundedBounds;
            if (grounded != null && TryBounds(grounded, out groundedBounds) && Mathf.Abs(groundedBounds.min.y - floorY) > 0.021f)
                return "BLOCK floating ground prop " + groundedName + " gap=" + (groundedBounds.min.y - floorY).ToString("F3");
        }

        string supportError;
        if (!ValidateSupport(room, "RecordsDesk", "RecordsTerminal", 0.021f, out supportError)) return "BLOCK " + supportError;
        if (!ValidateSupport(room, winnerCounterName(room), "PreparedPropertyBox_A", 0.021f, out supportError)) return "BLOCK " + supportError;
        if (!ValidateSupport(room, winnerCounterName(room), "PreparedPropertyBox_B", 0.021f, out supportError)) return "BLOCK " + supportError;

        string[] collisionNames =
        {
            "RecordsDesk", "LongDistributionCounter", "CompactDistributionCounter",
            "ArchiveShelf_A", "ArchiveShelf_B", "ArchiveShelf_L", "ClaimantBench", "ServiceWasteBin",
        };
        var collisionObjects = collisionNames.Select(name => FindDeep(room.transform, name)).Where(item => item != null).ToArray();
        for (int i = 0; i < collisionObjects.Length; i++)
        for (int j = i + 1; j < collisionObjects.Length; j++)
        {
            Bounds a;
            Bounds b;
            if (!TryBounds(collisionObjects[i], out a) || !TryBounds(collisionObjects[j], out b) || !a.Intersects(b)) continue;
            Vector3 min = Vector3.Max(a.min, b.min);
            Vector3 max = Vector3.Min(a.max, b.max);
            Vector3 depth = max - min;
            if (depth.x > 0.03f && depth.y > 0.03f && depth.z > 0.03f)
                return "BLOCK prop collision " + collisionObjects[i].name + " vs " + collisionObjects[j].name + " depth=" + depth;
        }

        Physics.SyncTransforms();
        for (int i = 0; i < 51; i++)
        {
            float z = Mathf.Lerp(1.5f, 12.5f, i / 50f);
            Vector3 lower = room.transform.TransformPoint(new Vector3(7f, 0.36f, z));
            Vector3 upper = room.transform.TransformPoint(new Vector3(7f, 1.44f, z));
            Collider[] hits = Physics.OverlapCapsule(lower, upper, 0.35f, ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider hit in hits)
            {
                if (hit == null || !hit.transform.IsChildOf(room.transform)) continue;
                if (hit.transform.IsChildOf(room.transform.Find("Floors"))) continue;
                if (hit.transform.IsChildOf(room.transform.Find("Ceils"))) continue;
                return "BLOCK central route hit " + GetPath(hit.transform, room.transform) + " sample=" + i;
            }
        }

        return "PASS root=4/4 hierarchy=8/8 doors=2 outward ceiling=full route=51/51";
    }

    private static string winnerCounterName(GameObject room)
    {
        return FindDeep(room.transform, "LongDistributionCounter") != null
            ? "LongDistributionCounter"
            : "CompactDistributionCounter";
    }

    private static bool ValidateSupport(GameObject room, string supportName, string itemName, float tolerance, out string error)
    {
        error = null;
        GameObject support = FindDeep(room.transform, supportName);
        GameObject item = FindDeep(room.transform, itemName);
        if (support == null || item == null)
        {
            error = "support pair missing " + supportName + " -> " + itemName;
            return false;
        }
        Bounds supportBounds;
        Bounds itemBounds;
        if (!TryBounds(support, out supportBounds) || !TryBounds(item, out itemBounds))
        {
            error = "support bounds missing " + supportName + " -> " + itemName;
            return false;
        }
        float gap = itemBounds.min.y - supportBounds.max.y;
        if (Mathf.Abs(gap) <= tolerance) return true;
        error = "unsupported prop " + itemName + " on " + supportName + " gap=" + gap.ToString("F3");
        return false;
    }

    private static bool TryCopyRootContract(GameObject donor, GameObject target, out string error)
    {
        error = null;
        string[] required = { "Unity.AI.Navigation.NavMeshSurface", "DunGen.Tile", "ApplyRenderingLayer", "PurrNet.NetworkTransform" };
        foreach (string type in required)
        {
            Component source = donor.GetComponents<Component>().FirstOrDefault(component => component != null && component.GetType().FullName == type);
            if (source == null) { error = "Contract donor is missing " + type; return false; }
            if (!UnityEditorInternal.ComponentUtility.CopyComponent(source) || !UnityEditorInternal.ComponentUtility.PasteComponentAsNew(target))
            { error = "Could not copy contract component " + type; return false; }
            Component copied = target.GetComponent(source.GetType());
            if (type == "Unity.AI.Navigation.NavMeshSurface")
            {
                var serialized = new SerializedObject(copied);
                SerializedProperty data = serialized.FindProperty("m_NavMeshData");
                if (data != null) data.objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(copied);
        }
        return true;
    }

    private static GameObject PlaceGrounded(GameObject source, Transform parent, string name, float x, float z, float yaw, float supportY)
    {
        GameObject clone = Clone(source, parent, name, new Vector3(x, 0f, z), yaw);
        Bounds bounds;
        if (TryBounds(clone, out bounds)) clone.transform.position += Vector3.up * (supportY - bounds.min.y);
        return clone;
    }

    private static GameObject PlaceWall(GameObject source, Transform parent, string name, float x, float z, float centerY, float yaw)
    {
        GameObject clone = Clone(source, parent, name, new Vector3(x, centerY, z), yaw);
        Bounds bounds;
        if (TryBounds(clone, out bounds))
        {
            Vector3 desired = parent.root.TransformPoint(new Vector3(x, centerY, z));
            clone.transform.position += desired - bounds.center;
        }
        return clone;
    }

    private static GameObject Clone(GameObject source, Transform parent, string name, Vector3 localPosition, float yaw)
    {
        GameObject clone = Object.Instantiate(source, parent);
        clone.name = name;
        clone.transform.localPosition = localPosition;
        clone.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        clone.transform.localScale = Vector3.one;
        return clone;
    }

    private static GameObject InstantiatePrefab(GameObject prefab, Transform parent, string name, Vector3 localPosition, float yaw)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.name = name;
        instance.transform.localPosition = localPosition;
        instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        instance.transform.localScale = Vector3.one;
        return instance;
    }

    private static Transform CreateRoot(Transform parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    private static GameObject FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        Transform found = all.FirstOrDefault(item => item != null && item.name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return found == null ? null : found.gameObject;
    }

    private static GameObject FindSceneObject(string name)
    {
        return Resources.FindObjectsOfTypeAll<GameObject>()
            .FirstOrDefault(item => item != null && !EditorUtility.IsPersistent(item) && item.scene == SceneManager.GetActiveScene() && item.name == name);
    }

    private static GameObject GetOrCreateRoot(Scene scene, string name)
    {
        GameObject root = scene.GetRootGameObjects().FirstOrDefault(item => item.name == name);
        if (root != null) return root;
        root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, scene);
        return root;
    }

    private static void DestroyNamedChildren(Transform parent, string name)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            if (parent.GetChild(i).name == name) Object.DestroyImmediate(parent.GetChild(i).gameObject);
    }

    private static float GetPreviewX(Scene scene, GameObject exclude, float gap)
    {
        bool found = false;
        float max = 0f;
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer.transform.IsChildOf(exclude.transform)) continue;
            max = found ? Mathf.Max(max, renderer.bounds.max.x) : renderer.bounds.max.x;
            found = true;
        }
        return found ? max + gap : 0f;
    }

    private static bool TryBounds(GameObject root, out Bounds bounds)
    {
        bounds = default(Bounds);
        bool found = false;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (found) return true;
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null) continue;
            if (!found) { bounds = collider.bounds; found = true; }
            else bounds.Encapsulate(collider.bounds);
        }
        return found;
    }

    private static bool OverlapsArea(Rect a, Rect b, float threshold)
    {
        float width = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
        float height = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        return width > 0f && height > 0f && width * height > threshold;
    }

    private static string GetPath(Transform item, Transform root)
    {
        var parts = new List<string>();
        Transform current = item;
        while (current != null && current != root) { parts.Add(current.name); current = current.parent; }
        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }

    private static string Failure(string reason)
    {
        return "{\"status\":\"BLOCK\",\"reason\":\"" + reason.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}";
    }

    private static string CaptureGeneratedRoom(bool top)
    {
        GameObject room = FindSceneObject(RoomName);
        if (room == null) return "BLOCK generated room not found";
        Transform ceils = room.transform.Find("Ceils");
        bool ceilingWasActive = ceils != null && ceils.gameObject.activeSelf;
        string relative = top ? TopCapturePath : IsometricCapturePath;
        string absolute = Path.Combine(Directory.GetParent(Application.dataPath).FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute));

        RenderTexture previous = RenderTexture.active;
        RenderTexture target = null;
        Texture2D texture = null;
        GameObject cameraObject = null;
        var hiddenObjects = new List<GameObject>();
        float previousReflectionIntensity = RenderSettings.reflectionIntensity;
        UnityEngine.Rendering.AmbientMode previousAmbientMode = RenderSettings.ambientMode;
        Color previousAmbientLight = RenderSettings.ambientLight;
        float previousAmbientIntensity = RenderSettings.ambientIntensity;
        try
        {
            Bounds bounds;
            if (!TryBounds(room, out bounds)) return "BLOCK generated room has no renderer bounds";
            if (ceils != null) ceils.gameObject.SetActive(false);
            if (top)
            {
                RenderSettings.reflectionIntensity = 0f;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.62f, 0.62f, 0.62f, 1f);
                RenderSettings.ambientIntensity = 1f;
            }
            Selection.activeGameObject = room;

            const int width = 1920;
            const int height = 1080;
            target = RenderTexture.GetTemporary(width, height, 24);
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            cameraObject = new GameObject("[StillWorking Room Capture Camera]");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.cameraType = CameraType.Game;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.075f, 0.085f, 0.10f, 1f);
            camera.allowHDR = false;
            camera.aspect = width / (float)height;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            Vector3 focus = new Vector3(bounds.center.x, 1.25f, bounds.center.z);
            if (top)
            {
                camera.orthographic = true;
                camera.orthographicSize = 8.15f;
                camera.transform.SetPositionAndRotation(focus + Vector3.up * 24f, Quaternion.Euler(90f, 180f, 0f));
            }
            else
            {
                camera.orthographic = false;
                camera.fieldOfView = 43f;
                Quaternion rotation = Quaternion.Euler(31f, -135f, 0f);
                camera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * 24f, rotation);
            }

            var lightObject = new GameObject("[StillWorking Room Capture Light]");
            lightObject.hideFlags = HideFlags.HideAndDontSave;
            lightObject.transform.SetParent(cameraObject.transform, false);
            lightObject.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = top ? 0f : 0.75f;
            light.shadows = top ? LightShadows.None : LightShadows.Soft;

            foreach (GameObject sceneRoot in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (sceneRoot == room.transform.root.gameObject || sceneRoot == cameraObject || !sceneRoot.activeSelf) continue;
                sceneRoot.SetActive(false);
                hiddenObjects.Add(sceneRoot);
            }
            if (room.transform.parent != null)
            {
                foreach (Transform sibling in room.transform.parent)
                {
                    if (sibling == room.transform || !sibling.gameObject.activeSelf) continue;
                    sibling.gameObject.SetActive(false);
                    hiddenObjects.Add(sibling.gameObject);
                }
            }
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(absolute, texture.EncodeToPNG());
            return "PASS capture=" + relative;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            return "BLOCK capture failed: " + exception.Message;
        }
        finally
        {
            if (ceils != null) ceils.gameObject.SetActive(ceilingWasActive);
            RenderSettings.reflectionIntensity = previousReflectionIntensity;
            RenderSettings.ambientMode = previousAmbientMode;
            RenderSettings.ambientLight = previousAmbientLight;
            RenderSettings.ambientIntensity = previousAmbientIntensity;
            foreach (GameObject hiddenObject in hiddenObjects)
                if (hiddenObject != null) hiddenObject.SetActive(true);
            RenderTexture.active = previous;
            if (target != null) RenderTexture.ReleaseTemporary(target);
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            if (texture != null) Object.DestroyImmediate(texture);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
        }
    }
}
