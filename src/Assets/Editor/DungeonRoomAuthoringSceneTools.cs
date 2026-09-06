using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Added specifically for the current dungeon room authoring work.
// Purpose: make Scene-based visual analysis and prefab staging repeatable while building/evaluating
// DunGen room tiles (especially NewPrison-style rooms) without relying on ad-hoc manual editor steps.
public static class DungeonRoomAuthoringSceneTools
{
    private const string ToolRoot = "Tools/Dungeon/Room Authoring/";
    private const string AssetRoot = "Assets/Dungeon/Room Authoring/";
    private const string PreviewRootName = "[DungeonRoomAuthoringPreview]";
    private const string PreviewLightName = "Preview Directional Light";
    private const string AiRoomGenScenePath = "Assets/SceneTemplateAssets/Scenes/AI Room Gen.unity";
    private const string FromScratchRoomName = "SecurityCheckpoint_FromScratch";
    private const string LegacyPrototypeName = "SecurityCheckpoint_Prototype";
    private const string FromScratchPrefabPath = "Assets/Prefabs/map_piece/NewPrison/AI_Room_Gen/SecurityCheckpoint_FromScratch.prefab";
    private const string LargeDoorPrefabPath = "Assets/Prefabs/map_piece/NewPrison/SomePicees/Door_LG_A.prefab";
    private const string CeilingLightPrefabPath = "Assets/Prefabs/map_piece/NewPrison/Lighting_Prefabs/Ceiling_Lights_DualSided.prefab";
    private const string AdministrativeSegregationPrefabPath = "Assets/Prefabs/map_piece/NewPrison/Tile_modified/AdminstrativeSegregation.prefab";
    private const float RowGap = 8f;
    private const int SnapshotWidth = 1920;
    private const int SnapshotHeight = 1080;

    private enum ViewPreset
    {
        Isometric,
        Top
    }

    [MenuItem(ToolRoot + "Preview Selected Prefabs In New Scene")]
    [MenuItem(AssetRoot + "Preview Selected Prefabs In New Scene")]
    private static void PreviewSelectedPrefabsInNewScene()
    {
        var prefabs = GetSelectedPrefabAssets();
        if (prefabs.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Room Authoring Preview",
                "Select one or more prefab assets in the Project window first.",
                "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PreparePreviewScene(scene);

        var previewRoot = GetOrCreatePreviewRoot(scene);
        var instances = InstantiatePrefabsInRow(prefabs, previewRoot.transform);
        Selection.objects = instances.Cast<Object>().ToArray();
        FocusObjects(instances, ViewPreset.Isometric);

        Debug.Log($"[DungeonRoomAuthoringSceneTools] Preview scene created with {instances.Count} prefab(s).", previewRoot);
    }

    [MenuItem(ToolRoot + "Stage Selected Prefabs In Active Scene")]
    [MenuItem(AssetRoot + "Stage Selected Prefabs In Active Scene")]
    private static void StageSelectedPrefabsInActiveScene()
    {
        var prefabs = GetSelectedPrefabAssets();
        if (prefabs.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Room Authoring Staging",
                "Select one or more prefab assets in the Project window first.",
                "OK");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid())
        {
            EditorUtility.DisplayDialog(
                "Room Authoring Staging",
                "No valid active scene is open.",
                "OK");
            return;
        }

        PreparePreviewScene(activeScene);

        var previewRoot = GetOrCreatePreviewRoot(activeScene);
        var instances = InstantiatePrefabsInRow(prefabs, previewRoot.transform);
        Selection.objects = instances.Cast<Object>().ToArray();
        FocusObjects(instances, ViewPreset.Isometric);

        EditorSceneManager.MarkSceneDirty(activeScene);
        Debug.Log($"[DungeonRoomAuthoringSceneTools] Staged {instances.Count} prefab(s) in active scene '{activeScene.name}'.", previewRoot);
    }

    [MenuItem(ToolRoot + "Arrange Selected Scene Objects In Row")]
    private static void ArrangeSelectedSceneObjectsInRow()
    {
        var sceneObjects = GetSelectedSceneObjects();
        if (sceneObjects.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Room Authoring Layout",
                "Select one or more scene objects in the Hierarchy first.",
                "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Arrange Dungeon Room Objects In Row");

        float cursorX = 0f;
        for (int i = 0; i < sceneObjects.Count; i++)
        {
            GameObject go = sceneObjects[i];
            Undo.RecordObject(go.transform, "Arrange room authoring objects");

            if (!TryGetHierarchyBounds(go, out Bounds bounds))
            {
                go.transform.position = new Vector3(cursorX, go.transform.position.y, go.transform.position.z);
                cursorX += RowGap;
                continue;
            }

            float deltaX = cursorX - bounds.min.x;
            go.transform.position += new Vector3(deltaX, 0f, 0f);
            cursorX += bounds.size.x + RowGap;
        }

        Undo.CollapseUndoOperations(undoGroup);
        FocusObjects(sceneObjects, ViewPreset.Isometric);
        Debug.Log($"[DungeonRoomAuthoringSceneTools] Arranged {sceneObjects.Count} scene object(s) in a comparison row.");
    }

    [MenuItem(ToolRoot + "Focus Selection/Isometric")]
    private static void FocusSelectionIsometric()
    {
        var sceneObjects = GetSelectedSceneObjects();
        if (sceneObjects.Count == 0)
        {
            Debug.LogWarning("[DungeonRoomAuthoringSceneTools] Select one or more scene objects to focus.");
            return;
        }

        FocusObjects(sceneObjects, ViewPreset.Isometric);
    }

    [MenuItem(ToolRoot + "Focus Selection/Top")]
    private static void FocusSelectionTop()
    {
        var sceneObjects = GetSelectedSceneObjects();
        if (sceneObjects.Count == 0)
        {
            Debug.LogWarning("[DungeonRoomAuthoringSceneTools] Select one or more scene objects to focus.");
            return;
        }

        FocusObjects(sceneObjects, ViewPreset.Top);
    }

    [MenuItem(ToolRoot + "Capture SceneView/Isometric")]
    private static void CaptureSceneViewIsometric()
    {
        CaptureSelectionSnapshot(ViewPreset.Isometric);
    }

    [MenuItem(ToolRoot + "Capture SceneView/Top")]
    private static void CaptureSceneViewTop()
    {
        CaptureSelectionSnapshot(ViewPreset.Top);
    }

    [MenuItem(ToolRoot + "Report Selection Summary")]
    private static void ReportSelectionSummary()
    {
        var sceneObjects = GetSelectedSceneObjects();
        if (sceneObjects.Count == 0)
        {
            Debug.LogWarning("[DungeonRoomAuthoringSceneTools] Select one or more scene objects in the Hierarchy first.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("[DungeonRoomAuthoringSceneTools] Selection Summary");
        sb.AppendLine($"Objects: {sceneObjects.Count}");

        for (int i = 0; i < sceneObjects.Count; i++)
        {
            AppendObjectSummary(sb, sceneObjects[i]);
        }

        Debug.Log(sb.ToString());
    }

    // Added for the current task: build a genuinely new room layout from reusable NewPrison pieces,
    // instead of only re-arranging props inside an existing room prefab.
    [MenuItem(ToolRoot + "Generate From Scratch/Security Checkpoint In AI Room Gen")]
    private static void GenerateSecurityCheckpointFromScratch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog(
                "Generate Security Checkpoint",
                "Exit Play Mode before generating an authoring-room prefab.",
                "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene scene = EditorSceneManager.OpenScene(AiRoomGenScenePath, OpenSceneMode.Single);
        EditorSceneManager.SetActiveScene(scene);
        PreparePreviewScene(scene);

        var previewRoot = GetOrCreatePreviewRoot(scene);
        DestroyPreviewChildrenIfExist(previewRoot.transform, FromScratchRoomName);
        DestroyPreviewChildrenIfExist(previewRoot.transform, LegacyPrototypeName);

        GameObject startRoom = GameObject.Find("StartRoom");
        GameObject corridorRoom = GameObject.Find("CorridorA");
        GameObject crossRoom = GameObject.Find("CrossRoom");
        GameObject officerRoom = GameObject.Find("OfficerRoom");

        if (startRoom == null || corridorRoom == null || crossRoom == null || officerRoom == null)
        {
            EditorUtility.DisplayDialog(
                "Generate Security Checkpoint",
                "AI Room Gen scene must contain StartRoom, CorridorA, CrossRoom, and OfficerRoom as reference donors.",
                "OK");
            return;
        }

        Transform startFloors = startRoom.transform.Find("Floors");
        Transform startWalls = startRoom.transform.Find("Walls");
        Transform startPillars = startRoom.transform.Find("Pillars");
        Transform startProps = startRoom.transform.Find("Props/No_Interactables");
        Transform corridorWalls = corridorRoom.transform.Find("Walls");
        Transform corridorProps = corridorRoom.transform.Find("Props/No_Interactables");
        Transform corridorFloors = corridorRoom.transform.Find("Floors");
        Transform crossProps = crossRoom.transform.Find("Props/No_Interactables");
        Transform crossFloors = crossRoom.transform.Find("Floors");
        Transform officerProps = officerRoom.transform.Find("Props/No_Interactables");
        Transform officerFloors = officerRoom.transform.Find("Floors");

        if (startFloors == null || startWalls == null || startPillars == null || startProps == null ||
            corridorWalls == null || corridorProps == null || corridorFloors == null ||
            crossProps == null || crossFloors == null || officerProps == null || officerFloors == null)
        {
            EditorUtility.DisplayDialog(
                "Generate Security Checkpoint",
                "One or more donor roots are missing (Floors/Walls/Pillars/Props).",
                "OK");
            return;
        }

        GameObject floorSource = FindFirstChildByName(startFloors, "Floor");
        GameObject wallShortSource = FindFirstChildByName(startWalls, "Wall_Interior_Uncapped");
        GameObject receptionSource = FindFirstChildByName(corridorWalls, "Wall_Interior_Reception");
        GameObject pillarWestSource = FindFirstChildByName(startPillars, "Pillar");
        GameObject pillarEastSource = FindFirstChildByName(startPillars, "Pillar (3)");

        if (floorSource == null || wallShortSource == null || receptionSource == null || pillarWestSource == null || pillarEastSource == null)
        {
            EditorUtility.DisplayDialog(
                "Generate Security Checkpoint",
                "Could not find required donor pieces for floors/walls/pillars.",
                "OK");
            return;
        }

        GameObject largeDoorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LargeDoorPrefabPath);
        GameObject ceilingLightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CeilingLightPrefabPath);
        GameObject administrativeSegregationPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AdministrativeSegregationPrefabPath);

        if (largeDoorPrefab == null || ceilingLightPrefab == null || administrativeSegregationPrefab == null)
        {
            EditorUtility.DisplayDialog(
                "Generate Security Checkpoint",
                "Could not load a required NewPrison prefab (large door / ceiling light / administrative segregation donor).",
                "OK");
            return;
        }

        Transform administrativeProps = administrativeSegregationPrefab.transform.Find("Props/No_Interactables");
        Transform administrativeFloors = administrativeSegregationPrefab.transform.Find("Floors");
        if (administrativeProps == null || administrativeFloors == null || administrativeFloors.childCount == 0)
        {
            EditorUtility.DisplayDialog(
                "Generate Security Checkpoint",
                "The administrative-segregation donor is missing its Floors or Props/No_Interactables root.",
                "OK");
            return;
        }

        float startBaseY = startFloors.childCount > 0 ? startFloors.GetChild(0).localPosition.y : 0f;
        float corridorBaseY = corridorFloors.childCount > 0 ? corridorFloors.GetChild(0).localPosition.y : 0f;
        float crossBaseY = crossFloors.childCount > 0 ? crossFloors.GetChild(0).localPosition.y : 0f;
        float officerBaseY = officerFloors.childCount > 0 ? officerFloors.GetChild(0).localPosition.y : 0f;
        float startX = GetNextPreviewStartX(previewRoot.transform, 18f);

        var roomRoot = new GameObject(FromScratchRoomName);
        roomRoot.transform.SetParent(previewRoot.transform, false);
        roomRoot.transform.localPosition = Vector3.zero;

        if (!TryCopyAuthoringRootContract(crossRoom, roomRoot, out string contractError))
        {
            EditorUtility.DisplayDialog(
                "Generate Security Checkpoint",
                $"Could not copy the NewPrison tile authoring contract from CrossRoom.\n\n{contractError}",
                "OK");
            Object.DestroyImmediate(roomRoot);
            return;
        }

        Transform floorsRoot = CreateChildTransform(roomRoot.transform, "Floors");
        Transform wallsRoot = CreateChildTransform(roomRoot.transform, "Walls");
        Transform doorwaysRoot = CreateChildTransform(roomRoot.transform, "Doorways");
        Transform ceilsRoot = CreateChildTransform(roomRoot.transform, "Ceils");
        Transform pillarsRoot = CreateChildTransform(roomRoot.transform, "Pillars");
        Transform propsRoot = CreateChildTransform(roomRoot.transform, "Props");
        CreateChildTransform(propsRoot, "Interactables");
        Transform propsNoInteractables = CreateChildTransform(propsRoot, "No_Interactables");
        Transform flowGuidesRoot = CreateChildTransform(propsNoInteractables, "01_FlowGuides");
        Transform controlBoothRoot = CreateChildTransform(propsNoInteractables, "02_ControlBooth");
        Transform inspectionRoot = CreateChildTransform(propsNoInteractables, "03_Inspection");
        Transform holdingRoot = CreateChildTransform(propsNoInteractables, "04_Holding");
        Transform securityDetailsRoot = CreateChildTransform(propsNoInteractables, "05_SecurityDetails");

        Vector3[] floorPositions =
        {
            new(7f, 0f, 7f),
            new(14f, 0f, 7f),
            new(7f, 0f, 14f),
            new(14f, 0f, 14f),
        };

        foreach (Vector3 localPosition in floorPositions)
            CloneSceneObject(floorSource, floorsRoot, "Floor", localPosition, Vector3.zero);

        Vector3[] ceilingPositions =
        {
            // The shared 7m module is rotated 90 degrees and extends along local -X/-Z.
            // With that rotation, Z=0/7 (not 7/14) covers the authored 0..14 footprint.
            new(7f, 3.5f, 0f),
            new(14f, 3.5f, 0f),
            new(7f, 3.5f, 7f),
            new(14f, 3.5f, 7f),
        };

        foreach (Vector3 localPosition in ceilingPositions)
            InstantiatePrefabAsset(ceilingLightPrefab, ceilsRoot, "Ceiling_Lights_DualSided", localPosition, new Vector3(0f, 90f, 0f));

        Vector3[] southShortWallPositions =
        {
            new(3.5f, 0f, 0f),
            new(14f, 0f, 0f),
        };

        foreach (Vector3 localPosition in southShortWallPositions)
            CloneSceneObject(wallShortSource, wallsRoot, "Wall_Interior_Uncapped", localPosition, Vector3.zero);

        Vector3[] northShortWallPositions =
        {
            new(0f, 0f, 14f),
            new(10.5f, 0f, 14f),
        };

        foreach (Vector3 localPosition in northShortWallPositions)
            CloneSceneObject(wallShortSource, wallsRoot, "Wall_Interior_Uncapped", localPosition, new Vector3(0f, 180f, 0f));

        float[] leftWallZ = { 0f, 3.5f, 7f, 10.5f };
        foreach (float z in leftWallZ)
            CloneSceneObject(wallShortSource, wallsRoot, "Wall_Interior_Uncapped", new Vector3(0f, 0f, z), new Vector3(0f, 90f, 0f));

        float[] rightWallZ = { 3.5f, 7f, 10.5f, 14f };
        foreach (float z in rightWallZ)
            CloneSceneObject(wallShortSource, wallsRoot, "Wall_Interior_Uncapped", new Vector3(14f, 0f, z), new Vector3(0f, 270f, 0f));

        // A single west-side glazed booth faces the screening lane. The opposite side remains a
        // visible inspection bay instead of mirroring another arbitrary office wall.
        CloneSceneObject(receptionSource, wallsRoot, "ControlBooth_ReceptionWindow", new Vector3(5.15f, 0f, 4.60f), new Vector3(0f, 90f, 0f));

        // DunGen doorways must face out of the room: -Z on the south edge, +Z on the north edge.
        InstantiatePrefabAsset(largeDoorPrefab, doorwaysRoot, "Door_LG_A", new Vector3(7f, 0f, 0f), new Vector3(0f, 180f, 0f));
        InstantiatePrefabAsset(largeDoorPrefab, doorwaysRoot, "Door_LG_A", new Vector3(7f, 0f, 14f), Vector3.zero);

        CloneSceneObject(pillarWestSource, pillarsRoot, "Pillar", new Vector3(0.2f, 0f, 3.16f), new Vector3(0f, 90f, 0f));
        CloneSceneObject(pillarWestSource, pillarsRoot, "Pillar", new Vector3(0.2f, 0f, 10.16f), new Vector3(0f, 90f, 0f));
        CloneSceneObject(pillarEastSource, pillarsRoot, "Pillar", new Vector3(13.81f, 0f, 3.16f), new Vector3(0f, 270f, 0f));
        CloneSceneObject(pillarEastSource, pillarsRoot, "Pillar", new Vector3(13.81f, 0f, 10.16f), new Vector3(0f, 270f, 0f));

        GameObject metalDetectorSource = FindFirstChildByName(startProps, "MetalDetector");
        GameObject beltBarrierSource = FindFirstChildByName(startProps, "BeltBarrier");
        GameObject beltBarrierPoleSource = FindFirstChildByName(startProps, "BeltBarrier_Pole");
        GameObject benchSource = FindFirstChildByName(startProps, "Bench_04");
        GameObject dividerLargeSource = FindFirstChildByName(crossProps, "Corridor_Divider_LG_Low");
        GameObject dividerSmallSource = FindFirstChildByName(crossProps, "Corridor_Divider_SM_Low");
        GameObject hazardStripeSource = FindFirstChildByName(crossProps, "Decal_stripe_BlackOrange");
        GameObject inspectionTableSource = FindFirstChildByName(administrativeProps, "Table_03");
        GameObject box0Source = FindFirstChildByName(officerProps, "Box_02");
        GameObject box1Source = FindFirstChildByName(officerProps, "Box_02 (1)");
        GameObject box2Source = FindFirstChildByName(officerProps, "Box_02 (2)");
        GameObject noticeboardSource = FindFirstChildByName(officerProps, "Noticeboard");
        GameObject fireAlarmSource = FindFirstChildByName(officerProps, "FireAlarm");
        GameObject pictureLogoSource = FindFirstChildByName(officerProps, "Picture_Logo");
        GameObject desk1Source = FindFirstChildByName(corridorProps, "Desk_01_CustomColor");
        GameObject pcSource = FindFirstChildByName(corridorProps, "PC_Complete");
        GameObject keypadSource = FindFirstChildByName(corridorProps, "Keypad");
        GameObject cameraSource = FindFirstChildByName(corridorProps, "CCTVCamera_Right");
        GameObject shelf9FullSource = FindFirstChildByName(corridorProps, "Shelf_09_Full");
        GameObject speakerSource = FindFirstChildByName(corridorProps, "Speaker_02");

        if (metalDetectorSource == null || beltBarrierSource == null || beltBarrierPoleSource == null || benchSource == null ||
            dividerLargeSource == null || dividerSmallSource == null || hazardStripeSource == null || inspectionTableSource == null ||
            box0Source == null || box1Source == null || box2Source == null ||
            noticeboardSource == null || fireAlarmSource == null || pictureLogoSource == null || desk1Source == null ||
            pcSource == null || keypadSource == null || cameraSource == null || shelf9FullSource == null || speakerSource == null)
        {
            EditorUtility.DisplayDialog(
                "Generate Security Checkpoint",
                "Could not find one or more screening-room donor pieces.",
                "OK");
            Object.DestroyImmediate(roomRoot);
            return;
        }

        // Theme: Contraband Intake. The floor markings, queue furniture, detector, booth window and
        // inspection bay form one readable south-to-north sequence. The centre is intentionally
        // sparse; storage is compressed to the north-east service edge.
        float[] stripeZ = { 4.00f, 8.31f };
        foreach (float z in stripeZ)
        {
            CloneSceneObject(hazardStripeSource, flowGuidesRoot, "LaneStripe_West", new Vector3(5.15f, 0.01f, z), new Vector3(0f, 90f, 0f));
            CloneSceneObject(hazardStripeSource, flowGuidesRoot, "LaneStripe_East", new Vector3(8.85f, 0.01f, z), new Vector3(0f, 90f, 0f));
        }

        CloneSceneObject(beltBarrierSource, flowGuidesRoot, "QueueBarrier_West", new Vector3(5.60f, beltBarrierSource.transform.localPosition.y - startBaseY, 2.30f), Vector3.zero);
        CloneSceneObject(beltBarrierPoleSource, flowGuidesRoot, "QueuePole_West", new Vector3(5.10f, beltBarrierPoleSource.transform.localPosition.y - startBaseY, 2.30f), Vector3.zero);
        CloneSceneObject(beltBarrierSource, flowGuidesRoot, "QueueBarrier_East", new Vector3(8.40f, beltBarrierSource.transform.localPosition.y - startBaseY, 2.30f), new Vector3(0f, 180f, 0f));
        CloneSceneObject(beltBarrierPoleSource, flowGuidesRoot, "QueuePole_East", new Vector3(8.90f, beltBarrierPoleSource.transform.localPosition.y - startBaseY, 2.30f), new Vector3(0f, 180f, 0f));
        CloneSceneObject(metalDetectorSource, flowGuidesRoot, "Primary_MetalDetector", new Vector3(7.00f, metalDetectorSource.transform.localPosition.y - startBaseY, 4.00f), Vector3.zero);

        CloneSceneObject(dividerLargeSource, flowGuidesRoot, "InspectionDivider_LG", new Vector3(8.85f, dividerLargeSource.transform.localPosition.y - crossBaseY, 4.60f), new Vector3(0f, 90f, 0f));
        CloneSceneObject(dividerSmallSource, flowGuidesRoot, "InspectionDivider_SM", new Vector3(8.85f, dividerSmallSource.transform.localPosition.y - crossBaseY, 7.10f), new Vector3(0f, 90f, 0f));

        CloneSceneObject(desk1Source, controlBoothRoot, "ControlDesk", new Vector3(3.75f, desk1Source.transform.localPosition.y - corridorBaseY, 6.20f), new Vector3(0f, 90f, 0f));
        CloneSceneObject(pcSource, controlBoothRoot, "ControlTerminal", new Vector3(3.75f, pcSource.transform.localPosition.y - corridorBaseY, 6.20f), new Vector3(0f, 90f, 0f));
        CloneSceneObject(keypadSource, controlBoothRoot, "LaneKeypad", new Vector3(5.32f, keypadSource.transform.localPosition.y - corridorBaseY, 7.55f), new Vector3(0f, 90f, 270f));

        CloneSceneObject(inspectionTableSource, inspectionRoot, "ContrabandInspectionTable", new Vector3(10.35f, 0f, 6.15f), new Vector3(0f, 90f, 0f));
        CloneSceneObject(box0Source, inspectionRoot, "TaggedEvidenceBox_A", new Vector3(10.20f, 0.76f, 5.85f), new Vector3(0f, 18f, 0f));
        CloneSceneObject(box1Source, inspectionRoot, "TaggedEvidenceBox_B", new Vector3(10.45f, 0.76f, 6.30f), new Vector3(0f, 342f, 0f));

        CloneSceneObject(benchSource, holdingRoot, "PostScreeningBench", new Vector3(0.75f, benchSource.transform.localPosition.y - startBaseY, 10.45f), new Vector3(0f, 90f, 0f));
        CloneSceneObject(noticeboardSource, holdingRoot, "ScreeningNoticeboard", new Vector3(0.22f, noticeboardSource.transform.localPosition.y - officerBaseY, 10.45f), new Vector3(0f, 90f, 0f));
        CloneSceneObject(shelf9FullSource, holdingRoot, "ReleasedPropertyShelf", new Vector3(11.15f, shelf9FullSource.transform.localPosition.y - corridorBaseY, 13.45f), new Vector3(0f, 180f, 0f));
        CloneSceneObject(box2Source, holdingRoot, "ReleasedPropertyBox", new Vector3(12.25f, 0f, 12.55f), new Vector3(0f, 28f, 0f));

        CloneSceneObject(cameraSource, securityDetailsRoot, "CCTV_Entry", new Vector3(0.42f, cameraSource.transform.localPosition.y - corridorBaseY, 1.05f), new Vector3(0f, 45f, 0f));
        CloneSceneObject(cameraSource, securityDetailsRoot, "CCTV_Exit", new Vector3(13.58f, cameraSource.transform.localPosition.y - corridorBaseY, 12.95f), new Vector3(0f, 225f, 0f));
        CloneSceneObject(speakerSource, securityDetailsRoot, "ScreeningSpeaker", new Vector3(5.28f, speakerSource.transform.localPosition.y - corridorBaseY, 7.90f), new Vector3(0f, 90f, 0f));
        CloneSceneObject(pictureLogoSource, securityDetailsRoot, "FacilityLogo", new Vector3(3.05f, pictureLogoSource.transform.localPosition.y - officerBaseY, 13.78f), new Vector3(0f, 180f, 0f));
        CloneSceneObject(fireAlarmSource, securityDetailsRoot, "FireAlarm", new Vector3(13.45f, fireAlarmSource.transform.localPosition.y - officerBaseY, 13.78f), new Vector3(0f, 180f, 0f));

        var generatedTile = roomRoot.GetComponent<DunGen.Tile>();
        if (generatedTile == null)
        {
            EditorUtility.DisplayDialog("Generate Security Checkpoint", "Generated room is missing DunGen.Tile.", "OK");
            Object.DestroyImmediate(roomRoot);
            return;
        }

        generatedTile.RecalculateBounds();
        Bounds generatedBounds = generatedTile.Bounds;
        if (generatedBounds.size.x <= 0f || generatedBounds.size.y <= 0f || generatedBounds.size.z <= 0f)
        {
            EditorUtility.DisplayDialog(
                "Generate Security Checkpoint",
                $"Generated DunGen bounds are invalid: center={generatedBounds.center}, size={generatedBounds.size}.",
                "OK");
            Object.DestroyImmediate(roomRoot);
            return;
        }
        EditorUtility.SetDirty(generatedTile);

        Directory.CreateDirectory(Path.GetDirectoryName(FromScratchPrefabPath) ?? "Assets/Prefabs/map_piece/NewPrison/AI_Room_Gen");
        PrefabUtility.SaveAsPrefabAssetAndConnect(roomRoot, FromScratchPrefabPath, InteractionMode.AutomatedAction);

        // Keep the reusable prefab at zero/identity. The comparison-row offset belongs only to
        // this scene instance and is applied after the prefab asset has been saved.
        roomRoot.transform.localPosition = new Vector3(startX, 0f, 0f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, AiRoomGenScenePath);
        AssetDatabase.SaveAssets();

        Selection.objects = new Object[] { roomRoot };
        FocusObjects(new[] { roomRoot }, ViewPreset.Isometric);

        Debug.Log($"[DungeonRoomAuthoringSceneTools] Built from-scratch room '{FromScratchRoomName}' at {FromScratchPrefabPath}", roomRoot);
    }

    [MenuItem(AssetRoot + "Preview Selected Prefabs In New Scene", true)]
    [MenuItem(AssetRoot + "Stage Selected Prefabs In Active Scene", true)]
    private static bool ValidateAssetPrefabSelectionCommands()
    {
        return GetSelectedPrefabAssets().Count > 0;
    }

    private static void CaptureSelectionSnapshot(ViewPreset preset)
    {
        var sceneObjects = GetSelectedSceneObjects();
        if (sceneObjects.Count == 0)
        {
            Debug.LogWarning("[DungeonRoomAuthoringSceneTools] Select one or more scene objects in the Hierarchy first.");
            return;
        }

        if (!FocusObjects(sceneObjects, preset))
            return;

        string outputPath = BuildSnapshotPath(preset);
        if (!TryCaptureSceneView(outputPath))
            return;

        Debug.Log($"[DungeonRoomAuthoringSceneTools] Saved {preset} SceneView snapshot to: {outputPath}");
    }

    private static void DestroyPreviewChildrenIfExist(Transform previewRoot, string childName)
    {
        if (previewRoot == null || string.IsNullOrWhiteSpace(childName))
            return;

        // A previously missing prefab can resolve after its asset is recreated. In that case the
        // scene may contain both the recovered instance and the newly generated one. Remove every
        // direct preview child with the generated-room name so one menu run always leaves one room.
        for (int i = previewRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = previewRoot.GetChild(i);
            if (child != null && string.Equals(child.name, childName, StringComparison.Ordinal))
                Object.DestroyImmediate(child.gameObject);
        }
    }

    private static bool TryCopyAuthoringRootContract(GameObject donorRoot, GameObject targetRoot, out string error)
    {
        error = null;
        if (donorRoot == null || targetRoot == null)
        {
            error = "The donor or target room root is missing.";
            return false;
        }

        string[] requiredComponentTypes =
        {
            "Unity.AI.Navigation.NavMeshSurface",
            "DunGen.Tile",
            "ApplyRenderingLayer",
            "PurrNet.NetworkTransform",
        };

        Component[] donorComponents = donorRoot.GetComponents<Component>();
        foreach (string requiredType in requiredComponentTypes)
        {
            Component donorComponent = donorComponents.FirstOrDefault(component =>
                component != null && string.Equals(component.GetType().FullName, requiredType, StringComparison.Ordinal));

            if (donorComponent == null)
            {
                error = $"The contract donor is missing required component '{requiredType}'.";
                return false;
            }

            if (!UnityEditorInternal.ComponentUtility.CopyComponent(donorComponent) ||
                !UnityEditorInternal.ComponentUtility.PasteComponentAsNew(targetRoot))
            {
                error = $"Failed to copy required component '{requiredType}'.";
                return false;
            }

            Component targetComponent = targetRoot.GetComponent(donorComponent.GetType());
            if (targetComponent == null)
            {
                error = $"Copied component '{requiredType}' was not found on the generated room.";
                return false;
            }

            // A source room's baked NavMesh belongs to that room's geometry. Preserve the proven
            // agent/build settings, but never carry the donor room's NavMeshData into this layout.
            if (string.Equals(requiredType, "Unity.AI.Navigation.NavMeshSurface", StringComparison.Ordinal))
            {
                var serializedComponent = new SerializedObject(targetComponent);
                SerializedProperty navMeshData = serializedComponent.FindProperty("m_NavMeshData");
                if (navMeshData != null)
                {
                    navMeshData.objectReferenceValue = null;
                    serializedComponent.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            EditorUtility.SetDirty(targetComponent);
        }

        return true;
    }

    private static float GetNextPreviewStartX(Transform previewRoot, float gap)
    {
        float maxX = 0f;
        bool hasBounds = false;

        if (previewRoot == null)
            return 0f;

        for (int i = 0; i < previewRoot.childCount; i++)
        {
            Transform child = previewRoot.GetChild(i);
            if (child == null || child.name == PreviewLightName)
                continue;

            if (!TryGetHierarchyBounds(child.gameObject, out Bounds bounds))
                continue;

            maxX = !hasBounds ? bounds.max.x : Mathf.Max(maxX, bounds.max.x);
            hasBounds = true;
        }

        return hasBounds ? maxX + gap : 0f;
    }

    private static Transform CreateChildTransform(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static GameObject FindFirstChildByName(Transform parent, string childName)
    {
        if (parent == null || string.IsNullOrWhiteSpace(childName))
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child != null && string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
                return child.gameObject;
        }

        return null;
    }

    private static GameObject CloneSceneObject(GameObject source, Transform parent, string cloneName, Vector3 localPosition, Vector3 localEulerAngles)
    {
        if (source == null)
            return null;

        var clone = Object.Instantiate(source, parent);
        clone.name = cloneName;
        clone.transform.localPosition = localPosition;
        clone.transform.localRotation = Quaternion.Euler(localEulerAngles);
        clone.transform.localScale = Vector3.one;
        return clone;
    }

    private static GameObject InstantiatePrefabAsset(GameObject prefabAsset, Transform parent, string instanceName, Vector3 localPosition, Vector3 localEulerAngles)
    {
        if (prefabAsset == null)
            return null;

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, parent);
        instance.name = instanceName;
        instance.transform.localPosition = localPosition;
        instance.transform.localRotation = Quaternion.Euler(localEulerAngles);
        instance.transform.localScale = Vector3.one;
        return instance;
    }

    private static List<GameObject> GetSelectedPrefabAssets()
    {
        return Selection.objects
            .OfType<GameObject>()
            .Where(IsPrefabAsset)
            .GroupBy(AssetDatabase.GetAssetPath)
            .Select(group => group.First())
            .ToList();
    }

    private static List<GameObject> GetSelectedSceneObjects()
    {
        var selected = Selection.gameObjects
            .Where(obj => obj != null && obj.scene.IsValid() && !EditorUtility.IsPersistent(obj))
            .ToList();

        if (selected.Count == 0)
            return selected;

        var selectedTransforms = new HashSet<Transform>(selected.Select(x => x.transform));
        return selected
            .Where(obj => obj.transform.parent == null || !selectedTransforms.Contains(obj.transform.parent))
            .ToList();
    }

    private static bool IsPrefabAsset(GameObject gameObject)
    {
        return gameObject != null && EditorUtility.IsPersistent(gameObject) && PrefabUtility.IsPartOfPrefabAsset(gameObject);
    }

    private static void PreparePreviewScene(Scene scene)
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.35f, 1f);
        RenderSettings.ambientIntensity = 1f;

        var previewRoot = GetOrCreatePreviewRoot(scene);
        Transform existingLight = previewRoot.transform.Find(PreviewLightName);
        if (existingLight != null)
            return;

        var lightObject = new GameObject(PreviewLightName);
        lightObject.transform.SetParent(previewRoot.transform);
        lightObject.transform.localPosition = Vector3.zero;
        lightObject.transform.localRotation = Quaternion.Euler(50f, -30f, 0f);

        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.15f;
        light.color = Color.white;
        light.shadows = LightShadows.Soft;
    }

    private static GameObject GetOrCreatePreviewRoot(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] != null && roots[i].name == PreviewRootName)
                return roots[i];
        }

        var root = new GameObject(PreviewRootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        return root;
    }

    private static List<GameObject> InstantiatePrefabsInRow(List<GameObject> prefabs, Transform parent)
    {
        var instances = new List<GameObject>(prefabs.Count);
        float cursorX = 0f;

        for (int i = 0; i < prefabs.Count; i++)
        {
            GameObject prefab = prefabs[i];
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(instance, "Stage dungeon room prefab");

            instance.name = prefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            if (TryGetHierarchyBounds(instance, out Bounds bounds))
            {
                float deltaX = cursorX - bounds.min.x;
                instance.transform.position += new Vector3(deltaX, 0f, 0f);
                cursorX += bounds.size.x + RowGap;
            }
            else
            {
                instance.transform.position = new Vector3(cursorX, 0f, 0f);
                cursorX += RowGap;
            }

            instances.Add(instance);
        }

        return instances;
    }

    private static bool FocusObjects(IEnumerable<GameObject> gameObjects, ViewPreset preset)
    {
        if (!TryGetCombinedBounds(gameObjects, out Bounds bounds))
        {
            Debug.LogWarning("[DungeonRoomAuthoringSceneTools] Could not determine bounds for the current selection.");
            return false;
        }

        SceneView sceneView = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
        if (sceneView == null)
        {
            Debug.LogError("[DungeonRoomAuthoringSceneTools] SceneView is not available.");
            return false;
        }

        sceneView.in2DMode = false;
        sceneView.orthographic = preset == ViewPreset.Top;
        sceneView.pivot = bounds.center;
        sceneView.rotation = preset == ViewPreset.Top
            ? Quaternion.Euler(90f, 180f, 0f)
            : Quaternion.Euler(25f, -135f, 0f);
        sceneView.size = Mathf.Max(6f, Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * 0.9f);
        sceneView.Repaint();
        return true;
    }

    private static bool TryCaptureSceneView(string outputPath)
    {
        SceneView sceneView = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
        if (sceneView == null || sceneView.camera == null)
        {
            Debug.LogError("[DungeonRoomAuthoringSceneTools] Could not access SceneView camera.");
            return false;
        }

        string directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        RenderTexture previousActive = RenderTexture.active;
        RenderTexture temporary = RenderTexture.GetTemporary(SnapshotWidth, SnapshotHeight, 24);
        var texture = new Texture2D(SnapshotWidth, SnapshotHeight, TextureFormat.RGBA32, false);
        var cameraObject = new GameObject("[Room Authoring Capture Camera]");
        cameraObject.hideFlags = HideFlags.HideAndDontSave;
        var captureCamera = cameraObject.AddComponent<Camera>();

        try
        {
            sceneView.Repaint();
            captureCamera.CopyFrom(sceneView.camera);
            captureCamera.transform.position = sceneView.camera.transform.position;
            captureCamera.transform.rotation = sceneView.camera.transform.rotation;
            captureCamera.cameraType = CameraType.Game;
            captureCamera.targetTexture = temporary;
            captureCamera.Render();

            RenderTexture.active = temporary;
            texture.ReadPixels(new Rect(0f, 0f, SnapshotWidth, SnapshotHeight), 0, 0);
            texture.Apply();

            File.WriteAllBytes(outputPath, texture.EncodeToPNG());
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DungeonRoomAuthoringSceneTools] Failed to capture SceneView: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
        finally
        {
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(temporary);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(texture);
        }
    }

    private static string BuildSnapshotPath(ViewPreset preset)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string snapshotFolder = Path.Combine(projectRoot, "Screenshots", "RoomAuthoring");
        string fileName = $"SceneView_{preset}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        return Path.Combine(snapshotFolder, fileName);
    }

    private static void AppendObjectSummary(StringBuilder sb, GameObject root)
    {
        TryGetHierarchyBounds(root, out Bounds bounds);

        int rendererCount = root.GetComponentsInChildren<Renderer>(true).Length;
        int colliderCount = root.GetComponentsInChildren<Collider>(true).Length;
        int lightCount = root.GetComponentsInChildren<Light>(true).Length;
        int doorwayLikeCount = CountComponentsContaining(root, "Doorway");
        int tileLikeCount = CountComponentsContaining(root, "DunGen.Tile");
        int directChildren = root.transform.childCount;

        sb.AppendLine($"- {root.name}");
        sb.AppendLine($"  Bounds Center: {bounds.center}");
        sb.AppendLine($"  Bounds Size: {bounds.size}");
        sb.AppendLine($"  Direct Children: {directChildren}");
        sb.AppendLine($"  Renderers: {rendererCount}, Colliders: {colliderCount}, Lights: {lightCount}");
        sb.AppendLine($"  DunGen Tile Components: {tileLikeCount}, Doorway-like Components: {doorwayLikeCount}");
        sb.AppendLine($"  Named Roots -> Floors:{HasDirectChild(root.transform, "Floors")}, Walls:{HasDirectChild(root.transform, "Walls")}, Props:{HasDirectChild(root.transform, "Props")}, Doorways:{HasDirectChild(root.transform, "Doorways")}, No_Interactables:{HasDirectChild(root.transform, "No_Interactables")}");
    }

    private static int CountComponentsContaining(GameObject root, string text)
    {
        return root.GetComponentsInChildren<Component>(true)
            .Count(component => component != null && component.GetType().FullName != null && component.GetType().FullName.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasDirectChild(Transform root, string childName)
    {
        if (root == null)
            return false;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child != null && string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool TryGetCombinedBounds(IEnumerable<GameObject> gameObjects, out Bounds bounds)
    {
        bool hasBounds = false;
        bounds = default;

        foreach (GameObject go in gameObjects)
        {
            if (go == null || !TryGetHierarchyBounds(go, out Bounds current))
                continue;

            if (!hasBounds)
            {
                bounds = current;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(current);
            }
        }

        return hasBounds;
    }

    private static bool TryGetHierarchyBounds(GameObject gameObject, out Bounds bounds)
    {
        bounds = default;
        if (gameObject == null)
            return false;

        var renderers = gameObject.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (bounds.size == Vector3.zero)
                bounds = renderer.bounds;
            else
                bounds.Encapsulate(renderer.bounds);
        }

        if (bounds.size != Vector3.zero)
            return true;

        var colliders = gameObject.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
                continue;

            if (bounds.size == Vector3.zero)
                bounds = collider.bounds;
            else
                bounds.Encapsulate(collider.bounds);
        }

        if (bounds.size != Vector3.zero)
            return true;

        bounds = new Bounds(gameObject.transform.position, Vector3.one);
        return true;
    }
}
