using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(HeldItemAdjustmentStudio))]
public sealed class HeldItemAdjustmentStudioEditor : Editor
{
    public const string StudioScenePath = "Assets/FPS/HeldItems/HeldItemAdjustmentStudio.unity";

    private const string PlayerPrefabPath = "Assets/FPS/Cyber_Generic.prefab";
    private const string CatalogPath = "Assets/FPS/HeldItems/HeldItemVisualCatalog.asset";
    private const string LocomotionPosePath =
        "Assets/RetargetedAnimations/locomotion/Unarmed/Cyber_Retarget_Unarmed_Idle.anim";
    private const string FistsPosePath = "Assets/RetargetedAnimations/Fist/Cyber_Retarget_Fists_Idle.anim";
    private const string WalkClipPath =
        "Assets/RetargetedAnimations/locomotion/Unarmed/Cyber_Retarget_Unarmed_Jog_Forward.anim";
    private const string RunClipPath =
        "Assets/RetargetedAnimations/locomotion/Cyber_Retarget_C_Unarmed_Sprint_Fwd.anim";
    private const string AttackClipPath =
        "Assets/RetargetedAnimations/Fist/Cyber_Retarget_Fists_Punch_UpperBodyCorrected.anim";
    private const string PlayerPreviewName = "Cyber_Generic_HeldItemPreview";

    private SerializedProperty _catalogProperty;
    private SerializedProperty _previewRootProperty;
    private SerializedProperty _selectedItemNameProperty;

    private string _itemSearch = string.Empty;
    private int _groupFilter;
    private bool _constrainScale = true;

    private void OnEnable()
    {
        _catalogProperty = serializedObject.FindProperty("catalog");
        _previewRootProperty = serializedObject.FindProperty("previewRoot");
        _selectedItemNameProperty = serializedObject.FindProperty("selectedItemName");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        HeldItemAdjustmentStudio studio = (HeldItemAdjustmentStudio)target;
        HeldItemVisualCatalog catalog = _catalogProperty.objectReferenceValue as HeldItemVisualCatalog;
        Transform previewRoot = _previewRootProperty.objectReferenceValue as Transform;

        EditorGUILayout.LabelField("Held Item Adjustment Studio", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "아이템 각도를 바꾸려면: 아래 [회전]을 누른 다음 Scene 뷰에서 기즈모를 드래그하세요.\n" +
            "Scene 카메라 자체는 Alt+우클릭으로 돌리고, 휠로 줌합니다. 끝나면 [현재 아이템 Catalog에 저장].",
            MessageType.Info);

        DrawViewToolbar(studio);
        DrawMotionToolbar(studio);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Catalog", catalog, typeof(HeldItemVisualCatalog), false);
            EditorGUILayout.ObjectField("middle_01_l Anchor", studio.HeldItemAnchor, typeof(Transform), true);
        }

        if (catalog == null || studio.HeldItemAnchor == null || catalog.Entries.Count == 0)
        {
            EditorGUILayout.HelpBox("Catalog 또는 middle_01_l Anchor 참조가 없습니다.", MessageType.Error);
            serializedObject.ApplyModifiedProperties();
            return;
        }

        DrawItemPicker(studio, catalog);

        EditorGUILayout.Space(8f);
        if (previewRoot != null)
            DrawTransformFields(studio, previewRoot);
        else
            EditorGUILayout.HelpBox("Preview가 없습니다. 다시 불러오기를 누르세요.", MessageType.Warning);

        EditorGUILayout.Space(6f);
        DrawActionButtons(studio);
        serializedObject.ApplyModifiedProperties();
    }

    private void DrawViewToolbar(HeldItemAdjustmentStudio studio)
    {
        EditorGUILayout.LabelField("보기", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (DrawToggleButton("1인칭 (게임 화면)", studio.ViewMode == HeldItemStudioView.FirstPerson))
            SetView(studio, HeldItemStudioView.FirstPerson);
        if (DrawToggleButton("3인칭 (다른 플레이어)", studio.ViewMode == HeldItemStudioView.ThirdPerson))
            SetView(studio, HeldItemStudioView.ThirdPerson);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawMotionToolbar(HeldItemAdjustmentStudio studio)
    {
        EditorGUILayout.LabelField("동작 (StartMap 주먹 상태)", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (DrawToggleButton("Idle", studio.MotionMode == HeldItemStudioMotion.Idle))
            SetMotion(studio, HeldItemStudioMotion.Idle);
        if (DrawToggleButton("걷기", studio.MotionMode == HeldItemStudioMotion.Walk))
            SetMotion(studio, HeldItemStudioMotion.Walk);
        if (DrawToggleButton("달리기", studio.MotionMode == HeldItemStudioMotion.Run))
            SetMotion(studio, HeldItemStudioMotion.Run);
        if (DrawToggleButton("공격", studio.MotionMode == HeldItemStudioMotion.Attack))
            SetMotion(studio, HeldItemStudioMotion.Attack);
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginChangeCheck();
        float time = EditorGUILayout.Slider("시간", studio.MotionNormalizedTime, 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(studio, "Scrub held item motion");
            studio.SetMotionNormalizedTime(time);
            ApplyMotionPose(studio);
        }

        EditorGUILayout.BeginHorizontal();
        bool playing = PoseDriver.IsMotionPlaying;
        if (DrawToggleButton(playing ? "정지" : "재생", playing))
            PoseDriver.IsMotionPlaying = !playing;
        if (GUILayout.Button("처음으로"))
        {
            studio.SetMotionNormalizedTime(0f);
            ApplyMotionPose(studio);
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawItemPicker(HeldItemAdjustmentStudio studio, HeldItemVisualCatalog catalog)
    {
        string[] groups = catalog.Entries
            .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.group))
            .Select(entry => entry.group)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(group => group, StringComparer.Ordinal)
            .ToArray();

        string[] filterLabels = new string[groups.Length + 1];
        filterLabels[0] = "전체";
        Array.Copy(groups, 0, filterLabels, 1, groups.Length);
        _groupFilter = Mathf.Clamp(_groupFilter, 0, filterLabels.Length - 1);
        _groupFilter = GUILayout.Toolbar(_groupFilter, filterLabels);
        _itemSearch = EditorGUILayout.TextField("검색", _itemSearch);

        HeldItemVisualCatalog.Entry[] filtered = catalog.Entries
            .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.itemName))
            .Where(entry => _groupFilter == 0
                || string.Equals(entry.group, filterLabels[_groupFilter], StringComparison.Ordinal))
            .Where(entry => string.IsNullOrWhiteSpace(_itemSearch)
                || entry.itemName.IndexOf(_itemSearch, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();

        if (filtered.Length == 0)
        {
            EditorGUILayout.HelpBox("필터에 맞는 아이템이 없습니다.", MessageType.Warning);
            return;
        }

        string[] labels = filtered
            .Select(entry => string.IsNullOrWhiteSpace(entry.group)
                ? entry.itemName
                : $"[{entry.group}] {entry.itemName}")
            .ToArray();

        int selectedIndex = Array.FindIndex(filtered,
            entry => string.Equals(entry.itemName, _selectedItemNameProperty.stringValue, StringComparison.Ordinal));
        if (selectedIndex < 0)
            EditorGUILayout.HelpBox("현재 아이템이 이 필터에 없습니다. 목록에서 고르면 바뀝니다.", MessageType.None);

        int displayedIndex = Mathf.Max(0, selectedIndex);
        EditorGUILayout.BeginHorizontal();
        bool prev = GUILayout.Button("<", GUILayout.Width(28f));
        EditorGUI.BeginChangeCheck();
        int newIndex = EditorGUILayout.Popup("Item", displayedIndex, labels);
        bool popupChanged = EditorGUI.EndChangeCheck();
        bool next = GUILayout.Button(">", GUILayout.Width(28f));
        EditorGUILayout.EndHorizontal();

        if (prev)
            newIndex = (displayedIndex + filtered.Length - 1) % filtered.Length;
        if (next)
            newIndex = (displayedIndex + 1) % filtered.Length;

        if ((prev || next || popupChanged)
            && newIndex >= 0 && newIndex < filtered.Length)
        {
            _selectedItemNameProperty.stringValue = filtered[newIndex].itemName;
            serializedObject.ApplyModifiedProperties();
            LoadSelectedPreview(studio);
            serializedObject.Update();
        }
    }

    private void DrawTransformFields(HeldItemAdjustmentStudio studio, Transform previewRoot)
    {
        EditorGUI.BeginChangeCheck();
        Vector3 localPosition = EditorGUILayout.Vector3Field("손 기준 위치", previewRoot.localPosition);
        Vector3 localEulerAngles = EditorGUILayout.Vector3Field("손 기준 회전", previewRoot.localEulerAngles);
        Vector3 localScale = EditorGUILayout.Vector3Field("크기", previewRoot.localScale);
        _constrainScale = EditorGUILayout.Toggle("비율 고정 스케일", _constrainScale);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(previewRoot, "Adjust held item preview");
            previewRoot.localPosition = localPosition;
            previewRoot.localEulerAngles = localEulerAngles;
            if (_constrainScale)
            {
                Vector3 current = previewRoot.localScale;
                if (!Mathf.Approximately(localScale.x, current.x))
                    localScale = Vector3.one * localScale.x;
                else if (!Mathf.Approximately(localScale.y, current.y))
                    localScale = Vector3.one * localScale.y;
                else if (!Mathf.Approximately(localScale.z, current.z))
                    localScale = Vector3.one * localScale.z;
            }

            previewRoot.localScale = HeldItemVisualPlacement.SanitizeScale(localScale);
            EditorSceneManager.MarkSceneDirty(previewRoot.gameObject.scene);
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Scene 기즈모", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("이동 (W)", GUILayout.Height(28f)))
            SelectPreviewForTool(studio, Tool.Move);
        if (GUILayout.Button("회전 (E)", GUILayout.Height(28f)))
            SelectPreviewForTool(studio, Tool.Rotate);
        if (GUILayout.Button("크기 (R)", GUILayout.Height(28f)))
            SelectPreviewForTool(studio, Tool.Scale);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+90 X"))
            RotatePreview(studio, new Vector3(90f, 0f, 0f));
        if (GUILayout.Button("+90 Y"))
            RotatePreview(studio, new Vector3(0f, 90f, 0f));
        if (GUILayout.Button("+90 Z"))
            RotatePreview(studio, new Vector3(0f, 0f, 90f));
        if (GUILayout.Button("회전 리셋"))
            ResetPreviewRotation(studio);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawActionButtons(HeldItemAdjustmentStudio studio)
    {
        if (GUILayout.Button("선택 아이템 다시 불러오기"))
            LoadSelectedPreview(studio);

        GUI.backgroundColor = new Color(0.55f, 1f, 0.65f);
        if (GUILayout.Button("현재 아이템 Catalog에 저장", GUILayout.Height(32f)))
            SaveSelectedPreview(studio);
        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("선택 아이템 위치/회전 초기화"))
            ResetSelectedPose(studio);

        if (GUILayout.Button("스튜디오 씬 저장"))
            EditorSceneManager.SaveScene(studio.gameObject.scene);
    }

    [MenuItem("Tools/Inventory/Open Held Item Adjustment Studio")]
    public static string OpenOrCreateStudio()
    {
        return OpenOrCreateStudioInternal(forceRecreate: false);
    }

    [MenuItem("Tools/Inventory/Rebuild Held Item Studio Scene")]
    public static string RebuildStudioScene()
    {
        return OpenOrCreateStudioInternal(forceRecreate: true);
    }

    public static string OpenOrCreateStudioInternal(bool forceRecreate)
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Exit Play Mode before opening the held-item studio.");

        Scene activeScene = SceneManager.GetActiveScene();
        bool alreadyInStudio = activeScene.IsValid() && activeScene.path == StudioScenePath;
        if (activeScene.IsValid() && activeScene.isDirty && !alreadyInStudio)
            throw new InvalidOperationException($"Active scene is dirty and was preserved: {activeScene.path}");

        HeldItemVisualCatalog catalog = AssetDatabase.LoadAssetAtPath<HeldItemVisualCatalog>(CatalogPath);
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        AnimationClip locomotionClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(LocomotionPosePath);
        AnimationClip fistsClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(FistsPosePath);
        AnimationClip walkClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkClipPath);
        AnimationClip runClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RunClipPath);
        AnimationClip attackClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AttackClipPath);
        if (catalog == null || playerPrefab == null)
            throw new InvalidOperationException("Held-item Catalog or Cyber_Generic prefab is missing.");
        if (locomotionClip == null || fistsClip == null)
            throw new InvalidOperationException("StartMap fist/unarmed idle clips are missing.");
        if (walkClip == null || runClip == null || attackClip == null)
            throw new InvalidOperationException("Walk/run/attack clips are missing.");

        Scene studioScene;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(StudioScenePath) != null && !forceRecreate)
        {
            studioScene = alreadyInStudio
                ? activeScene
                : EditorSceneManager.OpenScene(StudioScenePath, OpenSceneMode.Single);
        }
        else
        {
            if (forceRecreate && alreadyInStudio)
            {
                foreach (GameObject root in studioSceneRoots(activeScene))
                    UnityEngine.Object.DestroyImmediate(root);
                studioScene = activeScene;
            }
            else
            {
                studioScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        HeldItemAdjustmentStudio studio = EnsureStudio(studioScene, catalog, playerPrefab,
            locomotionClip, fistsClip, walkClip, runClip, attackClip);
        LoadSelectedPreview(studio);
        studio.ApplyCameraView();
        FrameHandInSceneView(studio);
        EditorSceneManager.SaveScene(studioScene, StudioScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeTransform = studio.PreviewRoot != null ? studio.PreviewRoot : studio.transform;
        return $"HELD_ITEM_STUDIO_OPEN path={StudioScenePath} item={studio.SelectedItemName} view={studio.ViewMode} items={catalog.Entries.Count}";
    }

    [MenuItem("Tools/Inventory/Place All Held Items In Hand")]
    public static string PlaceAllHeldItemsInHand()
    {
        HeldItemAdjustmentStudio studio = UnityEngine.Object.FindAnyObjectByType<HeldItemAdjustmentStudio>();
        if (studio == null || studio.Catalog == null)
            throw new InvalidOperationException("Open the held-item studio first.");

        ApplyGameplayPose(studio);
        Dictionary<string, float> startMapSizes = HeldItemAuthoringTool.CollectStartMapWorldMaxExtents();
        int placed = 0;
        foreach (HeldItemVisualCatalog.Entry entry in studio.Catalog.Entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.itemName) || entry.visualPrefab == null)
                continue;

            studio.Configure(studio.Catalog, studio.HeldItemAnchor, entry.itemName);
            LoadSelectedPreview(studio);
            if (studio.PreviewRoot == null || studio.PreviewRoot.childCount == 0)
                continue;

            Transform visual = studio.PreviewRoot.GetChild(0);
            startMapSizes.TryGetValue(entry.itemName, out float startMapSize);
            HeldItemVisualPlacement.ApplyHeldGrasp(studio.PreviewRoot, visual, entry.itemName, startMapSize);
            HeldItemVisualPlacement.Capture(studio.PreviewRoot, entry);
            placed++;
        }

        EditorUtility.SetDirty(studio.Catalog);
        AssetDatabase.SaveAssets();
        if (!string.IsNullOrWhiteSpace(studio.SelectedItemName))
            LoadSelectedPreview(studio);
        EditorSceneManager.SaveScene(studio.gameObject.scene);
        FrameHandInSceneView(studio);
        return $"HELD_ITEM_PLACED count={placed}";
    }

    public static void ApplyGameplayPose(HeldItemAdjustmentStudio studio)
    {
        ApplyMotionPose(studio);
    }

    public static void LoadSelectedPreview(HeldItemAdjustmentStudio studio)
    {
        if (studio == null || studio.Catalog == null || studio.HeldItemAnchor == null)
            return;

        string selectedName = studio.SelectedItemName;
        if (string.IsNullOrWhiteSpace(selectedName))
            selectedName = studio.Catalog.Entries.FirstOrDefault()?.itemName;

        if (!studio.Catalog.TryGet(selectedName, out HeldItemVisualCatalog.Entry entry)
            || entry == null || entry.visualPrefab == null)
        {
            Debug.LogError($"[HeldItemStudio] Visual is missing for '{selectedName}'.", studio);
            return;
        }

        for (int i = studio.HeldItemAnchor.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(studio.HeldItemAnchor.GetChild(i).gameObject);
        studio.SetPreviewRoot(null);

        GameObject pivot = new($"Preview_{selectedName}");
        Undo.RegisterCreatedObjectUndo(pivot, "Load held item preview");
        pivot.transform.SetParent(studio.HeldItemAnchor, false);

        GameObject visual = PrefabUtility.InstantiatePrefab(entry.visualPrefab, pivot.transform) as GameObject;
        if (visual == null)
            throw new InvalidOperationException($"Failed to instantiate studio preview for '{selectedName}'.");

        visual.name = "Visual";
        HeldItemVisualPlacement.Apply(pivot.transform, visual.transform, entry);

        studio.Configure(studio.Catalog, studio.HeldItemAnchor, selectedName);
        studio.SetPreviewRoot(pivot.transform);
        EditorUtility.SetDirty(studio);
        EditorSceneManager.MarkSceneDirty(studio.gameObject.scene);

        Selection.activeTransform = pivot.transform;
        PlaceFirstPersonCamera(studio);
        PlaceThirdPersonCamera(studio);
        studio.ApplyCameraView();
        SceneView.RepaintAll();
    }

    public static void SaveSelectedPreview(HeldItemAdjustmentStudio studio)
    {
        if (studio == null || studio.Catalog == null || studio.HeldItemAnchor == null
            || studio.PreviewRoot == null)
        {
            Debug.LogError("[HeldItemStudio] Studio references are incomplete.", studio);
            return;
        }

        if (!studio.Catalog.TryGet(studio.SelectedItemName, out HeldItemVisualCatalog.Entry entry)
            || entry == null)
        {
            Debug.LogError($"[HeldItemStudio] Catalog entry is missing: {studio.SelectedItemName}", studio);
            return;
        }

        Undo.RecordObject(studio.Catalog, "Save held item adjustment");
        HeldItemVisualPlacement.Capture(studio.PreviewRoot, entry);
        EditorUtility.SetDirty(studio.Catalog);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(studio.gameObject.scene);

        Debug.Log(
            $"[HeldItemStudio] Saved {entry.itemName}: pos={entry.localPosition:F4}, " +
            $"rotation={entry.localEulerAngles:F2}, scale={entry.localScale:F4}", studio.Catalog);
    }

    private static HeldItemAdjustmentStudio EnsureStudio(Scene scene, HeldItemVisualCatalog catalog,
        GameObject playerPrefab, AnimationClip locomotionClip, AnimationClip fistsClip,
        AnimationClip walkClip, AnimationClip runClip, AnimationClip attackClip)
    {
        HeldItemAdjustmentStudio studio = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<HeldItemAdjustmentStudio>(true))
            .FirstOrDefault();

        GameObject playerPreview = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == PlayerPreviewName);
        if (playerPreview == null)
        {
            playerPreview = PrefabUtility.InstantiatePrefab(playerPrefab, scene) as GameObject;
            if (playerPreview == null)
                throw new InvalidOperationException("Failed to instantiate Cyber_Generic for the studio.");

            playerPreview.name = PlayerPreviewName;
            playerPreview.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        HeldItemAdjustmentStudio.DisablePreviewGameplay(playerPreview);

        Transform middleFinger = playerPreview.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == "middle_01_l"
                && transform.parent != null && transform.parent.name == "middle_metacarpal_l");
        Transform anchor = middleFinger != null ? middleFinger.Find("HeldItemAnchor") : null;
        if (anchor == null)
            throw new InvalidOperationException("Cyber_Generic middle_01_l/HeldItemAnchor was not found.");

        Camera playerCamera = FindFirstPersonCamera(playerPreview);
        if (playerCamera == null)
            throw new InvalidOperationException("Cyber_Generic first-person Camera was not found.");

        GameObject leftoverStudioCamera = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == "StudioFirstPerson Camera");
        if (leftoverStudioCamera != null)
            UnityEngine.Object.DestroyImmediate(leftoverStudioCamera);

        Camera thirdPersonCamera = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
            .FirstOrDefault(camera => camera.name == "ThirdPerson Camera" || camera.name == "Studio Camera");
        if (thirdPersonCamera == null)
        {
            GameObject cameraObject = new("ThirdPerson Camera", typeof(Camera), typeof(AudioListener));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            thirdPersonCamera = cameraObject.GetComponent<Camera>();
        }
        else
        {
            thirdPersonCamera.gameObject.name = "ThirdPerson Camera";
        }

        PrepareStudioCamera(thirdPersonCamera, 45f);

        if (studio == null)
        {
            GameObject controllerObject = new("Held Item Adjustment Studio");
            SceneManager.MoveGameObjectToScene(controllerObject, scene);
            studio = controllerObject.AddComponent<HeldItemAdjustmentStudio>();
        }

        string itemName = string.IsNullOrWhiteSpace(studio.SelectedItemName)
            ? catalog.Entries[0].itemName
            : studio.SelectedItemName;
        studio.Configure(catalog, anchor, itemName);
        studio.SetPlayerPreview(playerPreview.transform, playerCamera, thirdPersonCamera);
        studio.SetPoseClips(locomotionClip, fistsClip, walkClip, runClip, attackClip);
        studio.SetStudioLayoutVersion(HeldItemAdjustmentStudio.CurrentStudioLayoutVersion);
        EditorUtility.SetDirty(studio);

        EnsureStudioEnvironment(scene);
        PoseDriver.SamplePose(studio);
        PlaceFirstPersonCamera(studio);
        PlaceThirdPersonCamera(studio);
        studio.ApplyCameraView();
        return studio;
    }

    private static void EnsureStudioEnvironment(Scene scene)
    {
        bool hasKeyLight = scene.GetRootGameObjects().Any(root => root.name == "Studio Key Light");
        if (!hasKeyLight)
        {
            GameObject keyLightObject = new("Studio Key Light", typeof(Light));
            SceneManager.MoveGameObjectToScene(keyLightObject, scene);
            Light keyLight = keyLightObject.GetComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.25f;
            keyLightObject.transform.rotation = Quaternion.Euler(35f, -35f, 0f);

            GameObject fillLightObject = new("Studio Fill Light", typeof(Light));
            SceneManager.MoveGameObjectToScene(fillLightObject, scene);
            Light fillLight = fillLightObject.GetComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 0.55f;
            fillLight.color = new Color(0.55f, 0.7f, 1f);
            fillLightObject.transform.rotation = Quaternion.Euler(20f, 145f, 0f);
        }

        GameObject floor = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Studio Floor");
        if (floor == null)
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Studio Floor";
            SceneManager.MoveGameObjectToScene(floor, scene);
            Collider floorCollider = floor.GetComponent<Collider>();
            if (floorCollider != null)
                UnityEngine.Object.DestroyImmediate(floorCollider);
        }

        floor.transform.position = Vector3.zero;
        floor.transform.localScale = Vector3.one * 1.2f;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.46f);
        RenderSettings.skybox = null;
    }

    private static readonly string[] FirstPersonHiddenRendererNames =
    {
        "SK_CSF_M_HEAD", "SK_CSF_M_EYES", "SK_CSF_M_LASHES", "SK_CSF_M_TEETH",
        "SK_CSF_M_CARUNCLE", "SK_CSF_M_HAIR_02", "SK_CSF_M_MASK",
        "SK_CSF_M_MASK_FRAME", "SK_CSF_M_MASK_GLASS"
    };

    private const int FirstPersonHideLayer = 31;

    private static readonly Vector3 PlayerCameraLocalPosition = new(-0.06784672f, -0.06542742f, 0.0008731568f);
    private const int PlayerCameraCullingMask = 262135;

    private static void PlaceFirstPersonCamera(HeldItemAdjustmentStudio studio, Camera posedPlayerCamera = null)
    {
        if (studio == null || studio.PlayerRoot == null)
            return;

        Camera playerCamera = posedPlayerCamera != null
            ? posedPlayerCamera
            : FindFirstPersonCamera(studio.PlayerRoot.gameObject);
        if (playerCamera == null)
            return;

        playerCamera.transform.localPosition = PlayerCameraLocalPosition;
        Vector3 facing = studio.PlayerRoot != null ? studio.PlayerRoot.forward : Vector3.forward;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.0001f)
            facing = Vector3.forward;
        playerCamera.transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
        playerCamera.fieldOfView = 90f;
        playerCamera.nearClipPlane = 0.01f;
        playerCamera.farClipPlane = 1000f;
        playerCamera.cullingMask = PlayerCameraCullingMask;
        playerCamera.clearFlags = CameraClearFlags.SolidColor;
        playerCamera.backgroundColor = new Color(0.16f, 0.18f, 0.22f, 1f);
        HideHeadFromFirstPerson(studio);
    }

    private static void HideHeadFromFirstPerson(HeldItemAdjustmentStudio studio)
    {
        if (studio.PlayerRoot == null || studio.FirstPersonCamera == null)
            return;

        foreach (Renderer renderer in studio.PlayerRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
                continue;
            bool hide = false;
            foreach (string name in FirstPersonHiddenRendererNames)
            {
                if (string.Equals(renderer.name, name, StringComparison.Ordinal))
                {
                    hide = true;
                    break;
                }
            }

            if (hide)
                renderer.gameObject.layer = FirstPersonHideLayer;
        }

        studio.FirstPersonCamera.cullingMask &= ~(1 << FirstPersonHideLayer);
        if (studio.ThirdPersonCamera != null)
            studio.ThirdPersonCamera.cullingMask |= 1 << FirstPersonHideLayer;
    }

    private static void PlaceThirdPersonCamera(HeldItemAdjustmentStudio studio)
    {
        if (studio == null || studio.ThirdPersonCamera == null || studio.PlayerRoot == null)
            return;

        Vector3 body = studio.PlayerRoot.position + Vector3.up * 1.05f;
        Vector3 lookTarget = studio.HeldItemAnchor != null
            ? Vector3.Lerp(body, studio.HeldItemAnchor.position, 0.55f)
            : body;
        studio.ThirdPersonCamera.transform.position = lookTarget + new Vector3(1.55f, 0.45f, -1.85f);
        studio.ThirdPersonCamera.transform.LookAt(lookTarget, Vector3.up);
        PrepareStudioCamera(studio.ThirdPersonCamera, 42f);
    }

    private static void PrepareStudioCamera(Camera camera, float fieldOfView)
    {
        camera.fieldOfView = fieldOfView;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 80f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.16f, 0.18f, 0.22f, 1f);
        camera.allowHDR = true;
        camera.allowMSAA = true;
        if (camera.GetComponent<UniversalAdditionalCameraData>() == null)
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
    }

    private static void SetView(HeldItemAdjustmentStudio studio, HeldItemStudioView view)
    {
        Undo.RecordObject(studio, "Change held item studio view");
        studio.SetViewMode(view);
        ApplyMotionPose(studio);
        EditorUtility.SetDirty(studio);
        SceneView.RepaintAll();
    }

    private static void SetMotion(HeldItemAdjustmentStudio studio, HeldItemStudioMotion motion)
    {
        Undo.RecordObject(studio, "Change held item motion");
        studio.SetMotionMode(motion);
        studio.SetMotionNormalizedTime(0f);
        ApplyMotionPose(studio);
        EditorUtility.SetDirty(studio);
        SceneView.RepaintAll();
    }

    public static void ApplyMotionPose(HeldItemAdjustmentStudio studio)
    {
        PoseDriver.SamplePose(studio);
        PlaceFirstPersonCamera(studio);
        PlaceThirdPersonCamera(studio);
        if (studio != null)
            studio.ApplyCameraView();
    }

    private static void FrameHandInSceneView(HeldItemAdjustmentStudio studio)
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null || studio == null || studio.HeldItemAnchor == null)
            return;

        sceneView.in2DMode = false;
        sceneView.orthographic = false;
        sceneView.isRotationLocked = false;
        Vector3 target = studio.HeldItemAnchor.position;
        sceneView.pivot = target;
        sceneView.rotation = Quaternion.Euler(14f, 148f, 0f);
        sceneView.size = 0.9f;
        sceneView.LookAt(target, sceneView.rotation, sceneView.size);
        sceneView.Repaint();
    }

    private static void SelectPreviewForTool(HeldItemAdjustmentStudio studio, Tool tool)
    {
        if (studio == null || studio.PreviewRoot == null)
            return;

        UnlockSceneView();
        Selection.activeTransform = studio.PreviewRoot;
        Tools.current = tool;
        Tools.pivotMode = PivotMode.Pivot;
        Tools.pivotRotation = PivotRotation.Local;
        SceneView.RepaintAll();
    }

    private static void UnlockSceneView()
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
            return;

        sceneView.in2DMode = false;
        sceneView.orthographic = false;
        sceneView.isRotationLocked = false;
    }

    private static void RotatePreview(HeldItemAdjustmentStudio studio, Vector3 euler)
    {
        if (studio.PreviewRoot == null)
            return;

        Undo.RecordObject(studio.PreviewRoot, "Rotate held item preview");
        studio.PreviewRoot.localRotation *= Quaternion.Euler(euler);
        EditorSceneManager.MarkSceneDirty(studio.gameObject.scene);
    }

    private static void ResetPreviewRotation(HeldItemAdjustmentStudio studio)
    {
        if (studio.PreviewRoot == null)
            return;

        Undo.RecordObject(studio.PreviewRoot, "Reset held item rotation");
        studio.PreviewRoot.localRotation = Quaternion.identity;
        EditorSceneManager.MarkSceneDirty(studio.gameObject.scene);
    }

    private static void FitPreviewToHand(HeldItemAdjustmentStudio studio)
    {
        if (studio.PreviewRoot == null)
            return;

        Transform visual = studio.PreviewRoot.childCount > 0 ? studio.PreviewRoot.GetChild(0) : studio.PreviewRoot;
        Undo.RecordObject(studio.PreviewRoot, "Fit held item to hand");
        studio.PreviewRoot.localScale = HeldItemVisualPlacement.FitPivotScaleToHand(visual);
        HeldItemVisualPlacement.CenterLocalBounds(visual);
        EditorSceneManager.MarkSceneDirty(studio.gameObject.scene);
    }

    private static void ResetSelectedPose(HeldItemAdjustmentStudio studio)
    {
        if (studio == null || studio.Catalog == null
            || !studio.Catalog.TryGet(studio.SelectedItemName, out HeldItemVisualCatalog.Entry entry)
            || entry == null)
        {
            return;
        }

        Undo.RecordObject(studio.Catalog, "Reset held item pose");
        entry.localPosition = Vector3.zero;
        entry.localEulerAngles = Vector3.zero;
        EditorUtility.SetDirty(studio.Catalog);
        AssetDatabase.SaveAssets();
        LoadSelectedPreview(studio);
    }

    private static Camera FindFirstPersonCamera(GameObject playerPreview)
    {
        if (playerPreview == null)
            return null;

        Camera[] cameras = playerPreview.GetComponentsInChildren<Camera>(true);
        Camera named = cameras.FirstOrDefault(camera => camera.name == "Camera");
        return named != null ? named : cameras.FirstOrDefault();
    }

    private static bool DrawToggleButton(string label, bool active)
    {
        Color previous = GUI.backgroundColor;
        if (active)
            GUI.backgroundColor = new Color(0.45f, 0.85f, 1f);
        bool pressed = GUILayout.Button(label, GUILayout.Height(26f));
        GUI.backgroundColor = previous;
        return pressed;
    }

    private static GameObject[] studioSceneRoots(Scene scene)
    {
        return scene.GetRootGameObjects();
    }

    [InitializeOnLoad]
    internal static class PoseDriver
    {
        private static bool _sampledThisSession;
        private static double _lastPlayTime;

        public static bool IsMotionPlaying { get; set; }

        static PoseDriver()
        {
            EditorApplication.update += Tick;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode)
                return;

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != StudioScenePath)
                return;

            HeldItemAdjustmentStudio studio = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<HeldItemAdjustmentStudio>(true))
                .FirstOrDefault();
            if (studio != null && studio.PlayerRoot != null)
                HeldItemAdjustmentStudio.DisablePreviewGameplay(studio.PlayerRoot.gameObject);
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (scene.path == StudioScenePath)
                _sampledThisSession = false;
        }

        private static void Tick()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != StudioScenePath)
            {
                IsMotionPlaying = false;
                return;
            }

            HeldItemAdjustmentStudio studio = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<HeldItemAdjustmentStudio>(true))
                .FirstOrDefault();
            if (studio == null || studio.PlayerRoot == null)
                return;

            if (EditorApplication.isPlaying)
                HeldItemAdjustmentStudio.DisablePreviewGameplay(studio.PlayerRoot.gameObject);

            if (IsMotionPlaying)
            {
                double now = EditorApplication.timeSinceStartup;
                if (_lastPlayTime <= 0d)
                    _lastPlayTime = now;
                float delta = (float)(now - _lastPlayTime);
                _lastPlayTime = now;
                AnimationClip clip = GetTimedClip(studio);
                float length = clip != null && clip.length > 0.01f ? clip.length : 1f;
                studio.SetMotionNormalizedTime(studio.MotionNormalizedTime + delta / length);
                ApplyMotionPose(studio);
                return;
            }

            _lastPlayTime = 0d;
            if (_sampledThisSession)
                return;

            ApplyMotionPose(studio);
            _sampledThisSession = true;
        }

        public static void SamplePose(HeldItemAdjustmentStudio studio)
        {
            if (studio == null || studio.PlayerRoot == null)
                return;

            Animator animator = studio.PlayerRoot.GetComponentInChildren<Animator>(true);
            if (animator == null)
                return;

            animator.enabled = false;
            GameObject root = animator.gameObject;
            float time = studio.MotionNormalizedTime;
            AnimationClip locomotion = studio.LocomotionPoseClip;
            AnimationClip fists = studio.GameplayPoseClip;

            switch (studio.MotionMode)
            {
                case HeldItemStudioMotion.Walk:
                    SampleClip(root, studio.WalkClip, time);
                    break;
                case HeldItemStudioMotion.Run:
                    SampleClip(root, studio.RunClip, time);
                    break;
                case HeldItemStudioMotion.Attack:
                    SampleClip(root, locomotion, 0f);
                    SampleClip(root, studio.AttackClip, time);
                    break;
                default:
                    SampleClip(root, locomotion, 0f);
                    SampleClip(root, fists, 0f);
                    break;
            }
        }

        private static AnimationClip GetTimedClip(HeldItemAdjustmentStudio studio)
        {
            return studio.MotionMode switch
            {
                HeldItemStudioMotion.Walk => studio.WalkClip,
                HeldItemStudioMotion.Run => studio.RunClip,
                HeldItemStudioMotion.Attack => studio.AttackClip,
                _ => studio.GameplayPoseClip
            };
        }

        private static void SampleClip(GameObject root, AnimationClip clip, float normalizedTime)
        {
            if (root == null || clip == null)
                return;

            float time = clip.length > 0f ? Mathf.Repeat(normalizedTime, 1f) * clip.length : 0f;
            clip.SampleAnimation(root, time);
        }
    }
}
