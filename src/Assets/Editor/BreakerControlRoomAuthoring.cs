using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Authors the NewPrison BreakerControlRoom from a proven one-door room shell,
/// then queues the existing V2 bake/rotation/navigation/production-flow pipeline.
/// </summary>
public static class BreakerControlRoomAuthoring
{
    private const string DonorPrefabPath =
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/Conference_Room.prefab";
    private const string SourcePrefabPath =
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/BreakerControlRoom.prefab";
    private const string V2PrefabPath =
        "Assets/Prefabs/map_piece/NewPrison/TEST/V2_BreakerControlRoom/BreakerControlRoom.prefab";

    private const string MaterialFolder =
        "Assets/Prefabs/map_piece/NewPrison/Materials/BreakerControlRoom";
    private const string AnimationFolder =
        "Assets/Animations/Dungeon/BreakerControlRoom";
    private const string ReportRelativePath =
        "Reports/RoomGeneration/BreakerControlRoom_Build.txt";
    private const string SourceReportRelativePath =
        "Reports/RoomGeneration/BreakerControlRoom_Source.txt";
    private const string SourceIsoRelativePath =
        "Screenshots/RoomGeneration/BreakerControlRoom_Source_Isometric.png";
    private const string SourceTopRelativePath =
        "Screenshots/RoomGeneration/BreakerControlRoom_Source_Top.png";
    private const string BakedP100RelativePath =
        "Screenshots/RoomGeneration/BreakerControlRoom_V2_P100.png";
    private const string BakedP0RelativePath =
        "Screenshots/RoomGeneration/BreakerControlRoom_V2_P0.png";

    private const string ModularProps =
        "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Prefabs/Props/";
    private const string ModularDecals =
        "Assets/KriptoFX/Magic Effects Pack v5/ModularPrisonPack/Prefabs/Decals/";

    private const string SwitchOnClipPath =
        "Assets/Universal Sound FX/BUTTONS/BUTTON_Plastic_Light_Switch_On_mono.wav";
    private const string SwitchOffClipPath =
        "Assets/Universal Sound FX/BUTTONS/BUTTON_Plastic_Light_Switch_Off_mono.wav";

    private static bool s_buildQueued;

    [InitializeOnLoadMethod]
    private static void ResumeQueuedBuildAfterDomainReload()
    {
        string reportPath = GetProjectAbsolutePath(ReportRelativePath);
        if (!File.Exists(reportPath))
            return;

        string firstLine;
        using (var reader = new StreamReader(reportPath))
            firstLine = reader.ReadLine();
        if (!string.Equals(firstLine, "QUEUED", StringComparison.Ordinal))
            return;

        s_buildQueued = true;
        EditorApplication.update -= TryRunQueuedBuild;
        EditorApplication.update += TryRunQueuedBuild;
    }

    [MenuItem("Tools/Dungeon V2/Rooms/Breaker Control Room/Create Source")]
    private static void CreateSourceMenu()
    {
        Debug.Log("[BreakerControlRoom] " + CreateOrReplaceSourceCli());
    }

    [MenuItem("Tools/Dungeon V2/Rooms/Breaker Control Room/Capture Source Preview")]
    private static void CaptureSourcePreviewMenu()
    {
        Debug.Log("[BreakerControlRoom] " + CaptureSourcePreviewCli());
    }

    [MenuItem("Tools/Dungeon V2/Rooms/Breaker Control Room/Queue Production Build")]
    private static void QueueProductionBuildMenu()
    {
        Debug.Log("[BreakerControlRoom] " + QueueProductionBuildCli());
    }

    public static string CreateOrReplaceSourceCli()
    {
        string preflight = ValidateSafeEditorState();
        if (preflight != null)
            return "BLOCK: " + preflight;

        GameObject root = null;
        try
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(DonorPrefabPath) == null)
                return "BLOCK: donor prefab was not found: " + DonorPrefabPath;

            EnsureAssetFolder(MaterialFolder);
            EnsureAssetFolder(AnimationFolder);
            EnsureAssetFolder(Path.GetDirectoryName(SourcePrefabPath)?.Replace('\\', '/'));

            if (AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath) != null &&
                !AssetDatabase.DeleteAsset(SourcePrefabPath))
            {
                return "BLOCK: existing source prefab could not be replaced: " + SourcePrefabPath;
            }

            if (!AssetDatabase.CopyAsset(DonorPrefabPath, SourcePrefabPath))
                return "BLOCK: failed to copy the Conference_Room donor.";

            AssetDatabase.ImportAsset(SourcePrefabPath, ImportAssetOptions.ForceSynchronousImport);
            root = PrefabUtility.LoadPrefabContents(SourcePrefabPath);
            root.name = "BreakerControlRoom";

            Transform props = RequireChild(root.transform, "Props");
            Transform noInteractables = RequireChild(props, "No_Interactables");
            DestroyChildren(noInteractables);
            Transform interactables = GetOrCreateChild(props, "Interactables");
            DestroyChildren(interactables);

            Material darkMetal = CreateOrUpdateMaterial(
                MaterialFolder + "/BreakerDarkMetal.mat", new Color(0.055f, 0.065f, 0.075f),
                0.82f, 0.38f, null);
            Material hazardYellow = CreateOrUpdateMaterial(
                MaterialFolder + "/BreakerHazardYellow.mat", new Color(0.92f, 0.58f, 0.035f),
                0.25f, 0.32f, null);
            Material leverRed = CreateOrUpdateMaterial(
                MaterialFolder + "/BreakerLeverRed.mat", new Color(0.62f, 0.025f, 0.018f),
                0.22f, 0.42f, new Color(2.8f, 0.035f, 0.02f));
            Material indicatorGreen = CreateOrUpdateMaterial(
                MaterialFolder + "/BreakerIndicatorGreen.mat", new Color(0.025f, 0.48f, 0.12f),
                0.12f, 0.42f, new Color(0.035f, 2.5f, 0.22f));

            BuildControlRoomProps(root, noInteractables);
            GameObject lever = BuildLeverStation(
                root, interactables, darkMetal, hazardYellow, leverRed, indicatorGreen);

            DunGen.Tile tile = root.GetComponent<DunGen.Tile>();
            if (tile == null)
                throw new InvalidOperationException("Copied room root has no DunGen.Tile component.");
            tile.RecalculateBounds();
            EditorUtility.SetDirty(tile);

            PrefabUtility.SaveAsPrefabAsset(root, SourcePrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string validation = ValidateSourcePrefab();
            WriteProjectText(SourceReportRelativePath, validation);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            return validation + "\nlever=" + GetHierarchyPath(lever.transform);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            return "ERROR: " + exception;
        }
        finally
        {
            if (root != null)
                PrefabUtility.UnloadPrefabContents(root);
        }
    }

    public static string CaptureSourcePreviewCli()
    {
        string preflight = ValidateSafeEditorState();
        if (preflight != null)
            return "BLOCK: " + preflight;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath) == null)
            return "BLOCK: source prefab has not been created.";

        return CaptureRoomPair(
            SourcePrefabPath,
            SourceIsoRelativePath,
            SourceTopRelativePath,
            bakedPowerPair: false);
    }

    public static string CaptureBakedPreviewCli()
    {
        string preflight = ValidateSafeEditorState();
        if (preflight != null)
            return "BLOCK: " + preflight;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(V2PrefabPath) == null)
            return "BLOCK: V2 prefab has not been built.";

        string capture = CaptureRoomPair(
            V2PrefabPath,
            BakedP100RelativePath,
            BakedP0RelativePath,
            bakedPowerPair: true);
        string difference = capture.StartsWith("PASS", StringComparison.Ordinal)
            ? MeasureCaptureDifference(BakedP100RelativePath, BakedP0RelativePath)
            : "SKIPPED";
        string result = capture + "\n" + difference;
        File.AppendAllText(GetProjectAbsolutePath(ReportRelativePath),
            "\n\n=== CAPTURE RECHECK ===\n" + result + "\n");
        return result;
    }

    public static string QueueProductionBuildCli()
    {
        string preflight = ValidateSafeEditorState();
        if (preflight != null)
            return "BLOCK: " + preflight;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath) == null)
            return "BLOCK: source prefab has not been created.";
        if (s_buildQueued)
            return "BLOCK: BreakerControlRoom production build is already queued.";

        s_buildQueued = true;
        WriteProjectText(ReportRelativePath,
            "QUEUED\nstartedUtc=" + DateTime.UtcNow.ToString("O") + "\nsource=" + SourcePrefabPath);

        EditorApplication.update -= TryRunQueuedBuild;
        EditorApplication.update += TryRunQueuedBuild;
        return "QUEUED: V2 P100/P0 bake, rotation packaging, navigation, Flow integration, validation, and captures.";
    }

    public static string RunProductionBuildNowCli()
    {
        EditorApplication.update -= TryRunQueuedBuild;
        s_buildQueued = true;
        RunProductionBuild();
        return "FINISHED: see " + ReportRelativePath;
    }

    private static void TryRunQueuedBuild()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || Lightmapping.isRunning)
            return;

        EditorApplication.update -= TryRunQueuedBuild;
        RunProductionBuild();
    }

    private static void RunProductionBuild()
    {
        var report = new StringBuilder();
        report.AppendLine("RUNNING");
        report.AppendLine("startedUtc=" + DateTime.UtcNow.ToString("O"));
        report.AppendLine("source=" + SourcePrefabPath);
        WriteProjectText(ReportRelativePath, report.ToString());

        try
        {
            string build = BAKEROTATETOOLV2.BuildRoomOneClickCli(SourcePrefabPath, false);
            report.AppendLine("\n=== V2 BUILD ===");
            report.AppendLine(build);
            WriteProjectText(ReportRelativePath, report.ToString());
            if (!build.StartsWith("PASS:", StringComparison.Ordinal))
                throw new InvalidOperationException("V2 one-click build did not pass.");

            string navigation = DungeonV2NavigationPrefabAuthoring.ApplyCli();
            report.AppendLine("\n=== NAVIGATION ===");
            report.AppendLine(navigation);
            WriteProjectText(ReportRelativePath, report.ToString());
            if (!navigation.StartsWith("PASS", StringComparison.Ordinal))
                throw new InvalidOperationException("V2 navigation authoring did not pass.");

            string flow = DungeonV2ProductionFlowAuthoring.ApplyCli();
            report.AppendLine("\n=== PRODUCTION FLOW ===");
            report.AppendLine(flow);
            WriteProjectText(ReportRelativePath, report.ToString());
            if (!flow.StartsWith("PASS", StringComparison.Ordinal))
                throw new InvalidOperationException("V2 production Flow integration did not pass.");

            string roomValidation = BAKEROTATETOOLV2.ValidateRoomCli(SourcePrefabPath);
            report.AppendLine("\n=== ROOM VALIDATION ===");
            report.AppendLine(roomValidation);

            string flowValidation = DungeonV2ProductionFlowAuthoring.ValidateCli();
            report.AppendLine("\n=== FLOW VALIDATION ===");
            report.AppendLine(flowValidation);

            string workspaceValidation = DungeonV2WorkspaceValidation.ValidateAllCli();
            report.AppendLine("\n=== WORKSPACE VALIDATION ===");
            report.AppendLine(workspaceValidation);

            string captures = CaptureRoomPair(
                V2PrefabPath,
                BakedP100RelativePath,
                BakedP0RelativePath,
                bakedPowerPair: true);
            report.AppendLine("\n=== CAPTURES ===");
            report.AppendLine(captures);

            bool passed = roomValidation.Contains("\"completed\": true") &&
                          flowValidation.StartsWith("PASS", StringComparison.Ordinal) &&
                          workspaceValidation.StartsWith("PASS", StringComparison.Ordinal) &&
                          captures.StartsWith("PASS", StringComparison.Ordinal);
            if (!passed)
                throw new InvalidOperationException("One or more final production gates did not pass.");

            report.Insert(0, "PASS\n");
            report.AppendLine("\ncompletedUtc=" + DateTime.UtcNow.ToString("O"));
            report.AppendLine("v2Prefab=" + V2PrefabPath);
            WriteProjectText(ReportRelativePath, report.ToString());
            Debug.Log("[BreakerControlRoom] Production build PASS\n" + report);
        }
        catch (Exception exception)
        {
            report.Insert(0, "ERROR\n");
            report.AppendLine("\n=== ERROR ===");
            report.AppendLine(exception.ToString());
            report.AppendLine("completedUtc=" + DateTime.UtcNow.ToString("O"));
            WriteProjectText(ReportRelativePath, report.ToString());
            Debug.LogException(exception);
        }
        finally
        {
            s_buildQueued = false;
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }

    private static void BuildControlRoomProps(GameObject roomRoot, Transform parent)
    {
        string panel1A = ModularProps + "ControlPanel_01_A.prefab";
        string panel1B = ModularProps + "ControlPanel_01_B.prefab";
        string panel2A = ModularProps + "ControlPanel_02_A.prefab";
        string panel2B = ModularProps + "ControlPanel_02_B.prefab";

        string[] backPanels = { panel1A, panel2A, panel1B, panel2B, panel1A, panel2A };
        float[] backPanelX = { -2.65f, -0.30f, 2.05f, 4.40f, 6.75f, 9.05f };
        for (int i = 0; i < backPanels.Length; i++)
        {
            AddPrefab(parent, backPanels[i], "BackWall_ControlPanel_" + (i + 1),
                new Vector3(backPanelX[i], -0.72f, -6.83f), Vector3.zero);
        }

        AddPrefab(parent, ModularProps + "Table_ControlPanel_01.prefab", "WestConsole_A",
            new Vector3(-3.45f, -2.03f, -2.05f), new Vector3(0f, 90f, 0f));
        AddPrefab(parent, ModularProps + "Table_ControlPanel_02.prefab", "WestConsole_B",
            new Vector3(-3.45f, -2.03f, -4.35f), new Vector3(0f, 90f, 0f));
        AddPrefab(parent, ModularProps + "Table_ControlPanel_02.prefab", "CentralConsole",
            new Vector3(5.20f, -2.03f, -3.45f), new Vector3(0f, 180f, 0f));

        AddPrefab(parent, ModularProps + "UtilityBox.prefab", "EastUtilityBox_A",
            new Vector3(9.55f, -2.03f, -1.55f), new Vector3(0f, 270f, 0f));
        AddPrefab(parent, ModularProps + "UtilityBox.prefab", "EastUtilityBox_B",
            new Vector3(9.55f, -2.03f, -3.30f), new Vector3(0f, 270f, 0f));
        AddPrefab(parent, ModularProps + "UtilityBox.prefab", "EastUtilityBox_C",
            new Vector3(9.55f, -2.03f, -5.05f), new Vector3(0f, 270f, 0f));

        AddPrefab(parent, ModularProps + "CCTV/CCTVMonitor_Mounted_04.prefab", "CCTV_Overview",
            new Vector3(8.75f, 1.15f, -6.56f), new Vector3(0f, 180f, 0f));
        AddPrefab(parent, ModularProps + "FireAlarm.prefab", "EmergencyAlarm",
            new Vector3(-3.82f, -0.42f, -0.30f), new Vector3(0f, 180f, 0f));
        AddPrefab(parent, ModularProps + "DocumentsSigns/Sign_CentralControl.prefab", "CentralControlSign",
            new Vector3(2.95f, 1.12f, -6.91f), Vector3.zero);
        AddPrefab(parent, ModularDecals + "Decal_CentralControl.prefab", "CentralControlWallMark",
            new Vector3(2.95f, 0.38f, -6.94f), Vector3.zero, new Vector3(0.82f, 0.82f, 0.82f));

        AddPrefab(parent, ModularProps + "Shelf_09_Full.prefab", "MaintenanceShelf",
            new Vector3(-3.30f, -2.03f, -6.25f), new Vector3(0f, 90f, 0f));
        AddPrefab(parent, ModularProps + "Box_02.prefab", "SparePartsBox_A",
            new Vector3(8.70f, -2.03f, -6.35f), new Vector3(0f, 12f, 0f));
        AddPrefab(parent, ModularProps + "Box_02.prefab", "SparePartsBox_B",
            new Vector3(8.05f, -2.03f, -6.45f), new Vector3(0f, 96f, 0f));

        foreach (Transform child in parent)
            SetRenderingLayerRecursive(child.gameObject, 2u);
    }

    private static GameObject BuildLeverStation(
        GameObject roomRoot,
        Transform parent,
        Material darkMetal,
        Material hazardYellow,
        Material leverRed,
        Material indicatorGreen)
    {
        var station = new GameObject("MainBreakerLever");
        station.transform.SetParent(parent, false);
        station.transform.localPosition = new Vector3(1.15f, -2.03f, -2.45f);
        station.layer = LayerMask.NameToLayer("Interactable");

        var stationCollider = station.AddComponent<BoxCollider>();
        stationCollider.center = new Vector3(0f, 0.70f, 0f);
        stationCollider.size = new Vector3(0.92f, 1.52f, 0.88f);

        CreatePrimitivePart(station.transform, "Pedestal", PrimitiveType.Cube,
            new Vector3(0f, 0.49f, 0f), Vector3.zero, new Vector3(0.72f, 0.98f, 0.62f), darkMetal);
        CreatePrimitivePart(station.transform, "HazardBand", PrimitiveType.Cube,
            new Vector3(0f, 0.76f, -0.318f), Vector3.zero, new Vector3(0.76f, 0.16f, 0.035f), hazardYellow);
        CreatePrimitivePart(station.transform, "TopPlate", PrimitiveType.Cube,
            new Vector3(0f, 1.02f, 0f), new Vector3(-8f, 0f, 0f), new Vector3(0.84f, 0.13f, 0.72f), darkMetal);

        Transform handlePivot = new GameObject("HandlePivot").transform;
        handlePivot.SetParent(station.transform, false);
        handlePivot.localPosition = new Vector3(0f, 1.09f, -0.02f);
        handlePivot.localRotation = Quaternion.Euler(-28f, 0f, 0f);
        CreatePrimitivePart(handlePivot, "LeverShaft", PrimitiveType.Cylinder,
            new Vector3(0f, 0.31f, 0f), Vector3.zero, new Vector3(0.055f, 0.31f, 0.055f), hazardYellow);
        CreatePrimitivePart(handlePivot, "LeverGrip", PrimitiveType.Sphere,
            new Vector3(0f, 0.65f, 0f), Vector3.zero, new Vector3(0.21f, 0.21f, 0.21f), leverRed);
        handlePivot.gameObject.AddComponent<DungeonDynamicProbeReceiver>();

        CreatePrimitivePart(station.transform, "PowerIndicator", PrimitiveType.Sphere,
            new Vector3(0.25f, 1.12f, -0.21f), Vector3.zero, new Vector3(0.10f, 0.055f, 0.10f), indicatorGreen);
        CreatePrimitivePart(station.transform, "WarningIndicator", PrimitiveType.Sphere,
            new Vector3(-0.25f, 1.12f, -0.21f), Vector3.zero, new Vector3(0.10f, 0.055f, 0.10f), leverRed);

        CreateHazardFloorMark(parent, hazardYellow, new Vector3(1.15f, -2.005f, -2.45f));

        AnimatorController controller = CreateLeverAnimatorController();
        Animator animator = station.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        AudioSource audioSource = station.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.minDistance = 1f;
        audioSource.maxDistance = 10f;

        DungeonTileLightSwitch lightSwitch = station.AddComponent<DungeonTileLightSwitch>();
        var serialized = new SerializedObject(lightSwitch);
        serialized.FindProperty("targetRoot").objectReferenceValue = roomRoot.transform;
        serialized.FindProperty("autoFindTargets").boolValue = true;
        serialized.FindProperty("includeInactiveTargets").boolValue = true;
        serialized.FindProperty("turnOffText").stringValue = "[F] Pull lever: lights off";
        serialized.FindProperty("turnOnText").stringValue = "[F] Pull lever: lights on";
        serialized.FindProperty("animator").objectReferenceValue = animator;
        serialized.FindProperty("audioSource").objectReferenceValue = audioSource;
        serialized.FindProperty("switchOnClip").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<AudioClip>(SwitchOnClipPath);
        serialized.FindProperty("switchOffClip").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<AudioClip>(SwitchOffClipPath);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        SetLayerRecursive(station, LayerMask.NameToLayer("Interactable"));
        SetRenderingLayerRecursive(station, 2u);
        return station;
    }

    private static void CreateHazardFloorMark(Transform parent, Material material, Vector3 center)
    {
        var markRoot = new GameObject("MainBreaker_HazardFloorMark");
        markRoot.transform.SetParent(parent, false);
        markRoot.transform.localPosition = center;
        const int stripeCount = 7;
        for (int i = 0; i < stripeCount; i++)
        {
            float x = -0.72f + i * 0.24f;
            GameObject stripe = CreatePrimitivePart(markRoot.transform, "Stripe_" + i,
                PrimitiveType.Cube, new Vector3(x, 0f, 0f), new Vector3(0f, 32f, 0f),
                new Vector3(0.10f, 0.018f, 1.38f), material);
            Object.DestroyImmediate(stripe.GetComponent<Collider>());
        }
    }

    private static AnimatorController CreateLeverAnimatorController()
    {
        string onClipPath = AnimationFolder + "/BreakerLever_On.anim";
        string offClipPath = AnimationFolder + "/BreakerLever_Off.anim";
        string controllerPath = AnimationFolder + "/BreakerLever.controller";
        DeleteAssetIfExists(onClipPath);
        DeleteAssetIfExists(offClipPath);
        DeleteAssetIfExists(controllerPath);

        AnimationClip onClip = CreateLeverClip(-28f);
        AnimationClip offClip = CreateLeverClip(28f);
        AssetDatabase.CreateAsset(onClip, onClipPath);
        AssetDatabase.CreateAsset(offClip, offClipPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        var parameter = new AnimatorControllerParameter
        {
            name = "IsOn",
            type = AnimatorControllerParameterType.Bool,
            defaultBool = true,
        };
        controller.AddParameter(parameter);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState onState = machine.AddState("On");
        AnimatorState offState = machine.AddState("Off");
        onState.motion = onClip;
        offState.motion = offClip;
        machine.defaultState = onState;

        AnimatorStateTransition toOff = onState.AddTransition(offState);
        toOff.hasExitTime = false;
        toOff.duration = 0.16f;
        toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsOn");
        AnimatorStateTransition toOn = offState.AddTransition(onState);
        toOn.hasExitTime = false;
        toOn.duration = 0.16f;
        toOn.AddCondition(AnimatorConditionMode.If, 0f, "IsOn");
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static AnimationClip CreateLeverClip(float xAngle)
    {
        var clip = new AnimationClip { frameRate = 30f };
        Quaternion rotation = Quaternion.Euler(xAngle, 0f, 0f);
        SetConstantCurve(clip, "HandlePivot", "m_LocalRotation.x", rotation.x);
        SetConstantCurve(clip, "HandlePivot", "m_LocalRotation.y", rotation.y);
        SetConstantCurve(clip, "HandlePivot", "m_LocalRotation.z", rotation.z);
        SetConstantCurve(clip, "HandlePivot", "m_LocalRotation.w", rotation.w);
        clip.EnsureQuaternionContinuity();
        return clip;
    }

    private static void SetConstantCurve(AnimationClip clip, string relativePath, string property, float value)
    {
        clip.SetCurve(relativePath, typeof(Transform), property, AnimationCurve.Constant(0f, 1f, value));
    }

    private static string ValidateSourcePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
        if (prefab == null)
            return "FAIL source prefab missing";

        DunGen.Tile[] tiles = prefab.GetComponentsInChildren<DunGen.Tile>(true);
        DunGen.Doorway[] doorways = prefab.GetComponentsInChildren<DunGen.Doorway>(true);
        DungeonTileLightSwitch[] switches = prefab.GetComponentsInChildren<DungeonTileLightSwitch>(true);
        Animator[] animators = prefab.GetComponentsInChildren<Animator>(true);
        Collider[] interactiveColliders = prefab.GetComponentsInChildren<Collider>(true)
            .Where(collider => collider.gameObject.layer == LayerMask.NameToLayer("Interactable"))
            .ToArray();
        bool rootTransformValid = prefab.transform.localPosition.sqrMagnitude < 0.000001f &&
                                  Quaternion.Angle(prefab.transform.localRotation, Quaternion.identity) < 0.001f &&
                                  (prefab.transform.localScale - Vector3.one).sqrMagnitude < 0.000001f;
        bool pass = tiles.Length == 1 && doorways.Length == 1 && switches.Length == 1 &&
                    animators.Length == 1 && interactiveColliders.Length > 0 && rootTransformValid;
        Vector3 bounds = tiles.Length == 1 ? tiles[0].Placement.LocalBounds.size : Vector3.zero;

        return string.Format(
            "{0} source={1} tiles={2} doorways={3} switches={4} animators={5} interactableColliders={6} " +
            "rootTransformValid={7} tileBounds={8} renderers={9} colliders={10} lights={11}",
            pass ? "PASS" : "FAIL", SourcePrefabPath, tiles.Length, doorways.Length, switches.Length,
            animators.Length, interactiveColliders.Length, rootTransformValid, bounds,
            prefab.GetComponentsInChildren<Renderer>(true).Length,
            prefab.GetComponentsInChildren<Collider>(true).Length,
            prefab.GetComponentsInChildren<Light>(true).Length);
    }

    private static string CaptureRoomPair(
        string prefabPath,
        string firstOutputRelativePath,
        string secondOutputRelativePath,
        bool bakedPowerPair)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            return "BLOCK: capture prefab missing: " + prefabPath;

        Scene original = EditorSceneManager.GetActiveScene();
        string originalPath = original.path;
        if (string.IsNullOrWhiteSpace(originalPath))
            return "BLOCK: the current scene must be saved before capture.";

        try
        {
            Scene preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject room = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
            room.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var cameraObject = new GameObject("__BreakerControlRoomCaptureCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.012f, 0.016f, 0.022f, 1f);
            camera.allowHDR = true;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 100f;

            if (bakedPowerPair)
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.012f, 0.014f, 0.018f);
                DungeonTileLightmapSwitcher switcher = room.GetComponent<DungeonTileLightmapSwitcher>();
                if (switcher == null || !switcher.HasAssignedBakeData)
                    return "BLOCK: V2 capture prefab has no assigned bake data.";

                PrepareSwitcherForEditModeCapture(switcher);
                ConfigureInteriorCamera(camera);
                switcher.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P100);
                RenderCamera(camera, firstOutputRelativePath, 1280, 720);
                switcher.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P0);
                RenderCamera(camera, secondOutputRelativePath, 1280, 720);
            }
            else
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.20f, 0.22f, 0.25f);
                var lightObject = new GameObject("__AuthoringPreviewKeyLight");
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.color = new Color(0.86f, 0.91f, 1f);
                lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

                ConfigureInteriorCamera(camera);
                RenderCamera(camera, firstOutputRelativePath, 1280, 720);

                Transform ceilings = room.transform.Find("Ceils");
                if (ceilings != null)
                    ceilings.gameObject.SetActive(false);
                camera.orthographic = true;
                camera.orthographicSize = 8.3f;
                camera.transform.position = new Vector3(2.95f, 14f, -3.5f);
                camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                RenderCamera(camera, secondOutputRelativePath, 1024, 1024);
            }

            return "PASS first=" + firstOutputRelativePath + " second=" + secondOutputRelativePath;
        }
        catch (Exception exception)
        {
            return "ERROR: capture failed: " + exception;
        }
        finally
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(originalPath) != null)
                EditorSceneManager.OpenScene(originalPath, OpenSceneMode.Single);
        }
    }

    private static void PrepareSwitcherForEditModeCapture(DungeonTileLightmapSwitcher switcher)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string[] methods = { "CacheRenderers", "CacheReflectionProbes", "DisableTileLights" };
        foreach (string methodName in methods)
        {
            MethodInfo method = typeof(DungeonTileLightmapSwitcher).GetMethod(methodName, flags);
            if (method == null)
                throw new MissingMethodException(typeof(DungeonTileLightmapSwitcher).FullName, methodName);
            method.Invoke(switcher, null);
        }
    }

    private static string MeasureCaptureDifference(string firstRelativePath, string secondRelativePath)
    {
        byte[] firstBytes = File.ReadAllBytes(GetProjectAbsolutePath(firstRelativePath));
        byte[] secondBytes = File.ReadAllBytes(GetProjectAbsolutePath(secondRelativePath));
        var first = new Texture2D(2, 2, TextureFormat.RGB24, false);
        var second = new Texture2D(2, 2, TextureFormat.RGB24, false);
        try
        {
            if (!first.LoadImage(firstBytes, false) || !second.LoadImage(secondBytes, false))
                return "FAIL captureDifference=images could not be decoded";
            if (first.width != second.width || first.height != second.height)
                return "FAIL captureDifference=image sizes differ";

            Color32[] a = first.GetPixels32();
            Color32[] b = second.GetPixels32();
            long absoluteChannelDifference = 0;
            int changedPixels = 0;
            int maxChannelDifference = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int r = Mathf.Abs(a[i].r - b[i].r);
                int g = Mathf.Abs(a[i].g - b[i].g);
                int blue = Mathf.Abs(a[i].b - b[i].b);
                absoluteChannelDifference += r + g + blue;
                maxChannelDifference = Mathf.Max(maxChannelDifference, r, g, blue);
                if (r + g + blue >= 9)
                    changedPixels++;
            }

            float meanAbsoluteDifference = absoluteChannelDifference / (float)(a.Length * 3 * 255);
            float changedPercent = changedPixels * 100f / a.Length;
            bool pass = meanAbsoluteDifference >= 0.005f && changedPercent >= 1f;
            return string.Format(
                "{0} captureDifference meanAbs={1:0.000000} changedPixels={2:0.00}% maxChannel={3}",
                pass ? "PASS" : "FAIL", meanAbsoluteDifference, changedPercent, maxChannelDifference);
        }
        finally
        {
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }
    }

    private static void ConfigureInteriorCamera(Camera camera)
    {
        camera.orthographic = false;
        camera.fieldOfView = 74f;
        camera.transform.position = new Vector3(1.15f, -0.48f, -0.72f);
        camera.transform.LookAt(new Vector3(3.05f, -0.63f, -4.15f));
    }

    private static void RenderCamera(Camera camera, string relativePath, int width, int height)
    {
        string absolutePath = GetProjectAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? GetProjectRoot());
        var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = renderTexture;
            camera.Render();
            RenderTexture.active = renderTexture;
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            texture.Apply(false, false);
            File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            renderTexture.Release();
            Object.DestroyImmediate(renderTexture);
            Object.DestroyImmediate(texture);
        }
    }

    private static GameObject AddPrefab(
        Transform parent,
        string assetPath,
        string name,
        Vector3 localPosition,
        Vector3 localEulerAngles,
        Vector3? localScale = null)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (asset == null)
            throw new InvalidOperationException("Required room prop was not found: " + assetPath);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent.gameObject.scene);
        instance.name = name;
        instance.transform.SetParent(parent, false);
        instance.transform.localPosition = localPosition;
        instance.transform.localRotation = Quaternion.Euler(localEulerAngles);
        instance.transform.localScale = localScale ?? Vector3.one;
        return instance;
    }

    private static GameObject CreatePrimitivePart(
        Transform parent,
        string name,
        PrimitiveType primitive,
        Vector3 localPosition,
        Vector3 localEulerAngles,
        Vector3 localScale,
        Material material)
    {
        GameObject part = GameObject.CreatePrimitive(primitive);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = Quaternion.Euler(localEulerAngles);
        part.transform.localScale = localScale;
        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        }
        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
            Object.DestroyImmediate(collider);
        return part;
    }

    private static Material CreateOrUpdateMaterial(
        string path,
        Color baseColor,
        float metallic,
        float smoothness,
        Color? emission)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("URP/Lit shader was not found.");
            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", baseColor);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }
        else
        {
            material.DisableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static string ValidateSafeEditorState()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return "Exit Play Mode before room authoring.";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            return "Wait for compilation and AssetDatabase refresh to finish.";
        if (Lightmapping.isRunning)
            return "Wait for the active light bake to finish.";
        Scene active = EditorSceneManager.GetActiveScene();
        if (active.IsValid() && active.isDirty)
            return "The active scene has unsaved changes.";
        return null;
    }

    private static void SetLayerRecursive(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    private static void SetRenderingLayerRecursive(GameObject root, uint mask)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            renderer.renderingLayerMask = mask;
        foreach (Light light in root.GetComponentsInChildren<Light>(true))
            light.renderingLayerMask = unchecked((int)mask);
    }

    private static Transform RequireChild(Transform root, string path)
    {
        Transform child = root.Find(path);
        if (child == null)
            throw new InvalidOperationException("Required donor hierarchy path missing: " + path);
        return child;
    }

    private static Transform GetOrCreateChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
            return child;
        var created = new GameObject(name);
        created.transform.SetParent(parent, false);
        return created.transform;
    }

    private static void DestroyChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(parent.GetChild(i).gameObject);
    }

    private static string GetHierarchyPath(Transform transform)
    {
        var path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }

    private static void EnsureAssetFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || AssetDatabase.IsValidFolder(path))
            return;
        string normalized = path.Replace('\\', '/');
        string parent = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
        string name = Path.GetFileName(normalized);
        EnsureAssetFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static void DeleteAssetIfExists(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            AssetDatabase.DeleteAsset(path);
    }

    private static void WriteProjectText(string relativePath, string content)
    {
        string absolutePath = GetProjectAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? GetProjectRoot());
        File.WriteAllText(absolutePath, content ?? string.Empty);
    }

    private static string GetProjectAbsolutePath(string relativePath)
    {
        return Path.Combine(GetProjectRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetProjectRoot()
    {
        return Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
    }
}
