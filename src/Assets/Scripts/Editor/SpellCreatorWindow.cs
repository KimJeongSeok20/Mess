using UnityEditor;
using UnityEngine;
using Demo.Scripts.Runtime.Item;

/// <summary>
/// 범용 스펠 생성 에디터 윈도우.
/// 메뉴: Tools > Spell System > Spell Creator
/// 
/// 입력값 넣고 "Create Spell" 누르면:
/// 1. SpellData SO
/// 2. SpellItem 프리팹 (FPS 뷰)
/// 3. WeaponData SO (무기 시스템 연동)
/// 4. WorldItem 프리팹 (바닥 드롭용) + 머티리얼
/// 5. weaponDatabase 자동 등록
/// 6. allItems 자동 등록
/// 전부 자동 생성.
/// </summary>
public class SpellCreatorWindow : EditorWindow
{
    private const string AssetFolder = "Assets/Scripts/Inventory/Spell/Data";
    private const string PrefabFolder = "Assets/Scripts/Inventory/Spell/Prefabs";

    // ─── Basic ───
    private string _spellName = "NewSpell";
    private SpellData.SpellType _spellType = SpellData.SpellType.Projectile;

    // ─── Charges / Cooldown ───
    private int _maxCharges = 5;
    private float _cooldown = 1.0f;

    // ─── Combat ───
    private int _damage = 80;
    private float _range = 50f;
    private float _radius = 0f;
    private int _healAmount = 0;
    private LayerMask _hitMask = ~0;

    // ─── VFX ───
    private GameObject _mainEffectPrefab;
    private GameObject _handEffectPrefab;
    private GameObject _characterEffectPrefab;
    private float _effectLifetime = 5f;
    private float _projectileVisualSpeed = 20f;

    // ─── SFX ───
    private AudioClip _castSound;
    private float _castVolume = 0.8f;

    // ─── World Item Visual ───
    private Color _worldItemColor = new Color(1f, 0.4f, 0.05f);
    private Color _worldItemEmission = new Color(1f, 0.3f, 0f);
    private float _worldItemScale = 0.3f;

    // ─── State ───
    private Vector2 _scrollPos;
    private string _statusMessage = "";
    private MessageType _statusType = MessageType.None;

    [MenuItem("Tools/Spell System/Spell Creator")]
    public static void ShowWindow()
    {
        var window = GetWindow<SpellCreatorWindow>("Spell Creator");
        window.minSize = new Vector2(400, 600);
    }

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.LabelField("Spell Creator", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        // ─── Basic Info ───
        EditorGUILayout.LabelField("Basic Info", EditorStyles.boldLabel);
        _spellName = EditorGUILayout.TextField("Spell Name", _spellName);
        _spellType = (SpellData.SpellType)EditorGUILayout.EnumPopup("Spell Type", _spellType);

        EditorGUILayout.Space(8);

        // ─── Charges / Cooldown ───
        EditorGUILayout.LabelField("Charges / Cooldown", EditorStyles.boldLabel);
        _maxCharges = EditorGUILayout.IntSlider("Max Charges", _maxCharges, 1, 20);
        _cooldown = EditorGUILayout.Slider("Cooldown (sec)", _cooldown, 0f, 10f);

        EditorGUILayout.Space(8);

        // ─── Combat (타입별 표시) ───
        EditorGUILayout.LabelField("Combat", EditorStyles.boldLabel);
        if (_spellType == SpellData.SpellType.Projectile || _spellType == SpellData.SpellType.AreaOfEffect)
        {
            _damage = EditorGUILayout.IntSlider("Damage", _damage, 0, 500);
            _range = EditorGUILayout.Slider("Range (m)", _range, 5f, 200f);
            _hitMask = LayerMaskField("Hit Mask", _hitMask);
        }
        if (_spellType == SpellData.SpellType.AreaOfEffect)
        {
            _radius = EditorGUILayout.Slider("AoE Radius (m)", _radius, 1f, 30f);
        }
        if (_spellType == SpellData.SpellType.Heal)
        {
            _healAmount = EditorGUILayout.IntSlider("Heal Amount", _healAmount, 1, 500);
        }

        EditorGUILayout.Space(8);

        // ─── VFX ───
        EditorGUILayout.LabelField("VFX (MagicFX5 Prefabs)", EditorStyles.boldLabel);
        _mainEffectPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Main Effect", _mainEffectPrefab, typeof(GameObject), false);
        _handEffectPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Hand Effect", _handEffectPrefab, typeof(GameObject), false);
        _characterEffectPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Character Effect", _characterEffectPrefab, typeof(GameObject), false);
        _effectLifetime = EditorGUILayout.Slider("Effect Lifetime", _effectLifetime, 0.5f, 15f);
        _projectileVisualSpeed = EditorGUILayout.Slider("Visual Speed", _projectileVisualSpeed, 5f, 100f);

        EditorGUILayout.Space(8);

        // ─── SFX ───
        EditorGUILayout.LabelField("SFX", EditorStyles.boldLabel);
        _castSound = (AudioClip)EditorGUILayout.ObjectField(
            "Cast Sound", _castSound, typeof(AudioClip), false);
        _castVolume = EditorGUILayout.Slider("Cast Volume", _castVolume, 0f, 1f);

        EditorGUILayout.Space(8);

        // ─── World Item Visual ───
        EditorGUILayout.LabelField("World Item (Pickup Visual)", EditorStyles.boldLabel);
        _worldItemColor = EditorGUILayout.ColorField("Base Color", _worldItemColor);
        _worldItemEmission = EditorGUILayout.ColorField(
            new GUIContent("Emission Color"), _worldItemEmission, true, false, true);
        _worldItemScale = EditorGUILayout.Slider("Visual Scale", _worldItemScale, 0.1f, 1f);

        EditorGUILayout.Space(16);

        // ─── Validation ───
        bool valid = ValidateInput();

        EditorGUI.BeginDisabledGroup(!valid);
        if (GUILayout.Button("Create Spell", GUILayout.Height(36)))
        {
            CreateSpell();
        }
        EditorGUI.EndDisabledGroup();

        // ─── Status ───
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(_statusMessage, _statusType);
        }

        EditorGUILayout.EndScrollView();
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(_spellName))
        {
            _statusMessage = "Spell Name을 입력하세요.";
            _statusType = MessageType.Warning;
            return false;
        }

        // 이미 존재하는 에셋 확인
        string spellDataPath = $"{AssetFolder}/{_spellName}_SpellData.asset";
        if (AssetDatabase.LoadAssetAtPath<SpellData>(spellDataPath) != null)
        {
            _statusMessage = $"'{_spellName}_SpellData.asset'이 이미 존재합니다. 다른 이름을 사용하세요.";
            _statusType = MessageType.Warning;
            return false;
        }

        _statusMessage = "";
        _statusType = MessageType.None;
        return true;
    }

    private void CreateSpell()
    {
        EnsureFolder(AssetFolder);
        EnsureFolder(PrefabFolder);

        string safeName = _spellName.Replace(" ", "");

        // ──────────────────────────────────────
        // 1) SpellData SO
        // ──────────────────────────────────────
        var spellData = ScriptableObject.CreateInstance<SpellData>();
        spellData.spellName = _spellName;
        spellData.spellType = _spellType;
        spellData.maxCharges = _maxCharges;
        spellData.cooldownPerCast = _cooldown;
        spellData.damage = _damage;
        spellData.range = _range;
        spellData.radius = _radius;
        spellData.hitMask = _hitMask;
        spellData.healAmount = _healAmount;
        spellData.mainEffectPrefab = _mainEffectPrefab;
        spellData.handEffectPrefab = _handEffectPrefab;
        spellData.characterEffectPrefab = _characterEffectPrefab;
        spellData.effectLifetime = _effectLifetime;
        spellData.projectileVisualSpeed = _projectileVisualSpeed;
        spellData.castSound = _castSound;
        spellData.castVolume = _castVolume;

        string spellDataPath = $"{AssetFolder}/{safeName}_SpellData.asset";
        AssetDatabase.CreateAsset(spellData, spellDataPath);
        Debug.Log($"[SpellCreator] SpellData: {spellDataPath}");

        // ──────────────────────────────────────
        // 2) SpellItem Prefab (FPS 뷰)
        // ──────────────────────────────────────
        var spellItemGo = new GameObject($"{safeName}_SpellItem");
        var spellItem = spellItemGo.AddComponent<SpellItem>();

        var so = new SerializedObject(spellItem);
        SetProperty(so, "_spellData", spellData);

        // Fist의 unarmed controller + equipMotion 가져와서 SpellItem에도 적용
        // (총→스펠 전환 시 이전 무기의 animator controller가 리셋되도록)
        var unarmedController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            "Assets/FPS/Weapon/Fist v/FPSAnimator_Unarmed_Generic.overrideController");
        if (unarmedController == null)
            unarmedController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/scriptable-animation-system-main/Assets/Demo/Prefabs/Fists/FPSAnimator_Unarmed_Generic.overrideController");
        SetProperty(so, "overrideController", unarmedController);

        var fistEquipMotion = AssetDatabase.LoadAssetAtPath<Object>(
            AssetDatabase.GUIDToAssetPath("1b41d61afa9fab64192ea8bcb101c746"));
        if (fistEquipMotion != null)
            SetProperty(so, "equipMotion", fistEquipMotion);

        so.ApplyModifiedPropertiesWithoutUndo();

        string spellItemPath = $"{PrefabFolder}/{safeName}_SpellItem.prefab";
        var spellItemPrefab = PrefabUtility.SaveAsPrefabAsset(spellItemGo, spellItemPath);
        DestroyImmediate(spellItemGo);
        Debug.Log($"[SpellCreator] SpellItem: {spellItemPath}");

        // ──────────────────────────────────────
        // 3) WeaponData SO (무기 시스템 연동)
        // ──────────────────────────────────────
        var weaponData = ScriptableObject.CreateInstance<WeaponData>();
        weaponData.weaponName = _spellName;
        weaponData.weaponInstancePrefab = spellItemPrefab;
        weaponData.magazineSize = _maxCharges;
        weaponData.fireSimulation = WeaponData.FireSimulationType.Hitscan;
        weaponData.damage = _damage;
        weaponData.range = _range;
        weaponData.serverFireRateRpm = Mathf.Max(1f, 60f / Mathf.Max(0.1f, _cooldown));

        string weaponDataPath = $"{AssetFolder}/{safeName}_WeaponData.asset";
        AssetDatabase.CreateAsset(weaponData, weaponDataPath);
        Debug.Log($"[SpellCreator] WeaponData: {weaponDataPath}");

        // ──────────────────────────────────────
        // 4) WorldItem Prefab (바닥 아이템)
        // ──────────────────────────────────────
        var worldItemGo = new GameObject($"{safeName}_WorldItem");

        // Visual
        var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "Visual";
        visual.transform.SetParent(worldItemGo.transform);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = Vector3.one * _worldItemScale;
        DestroyImmediate(visual.GetComponent<Collider>());

        // Material
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", _worldItemColor);
        mat.SetColor("_EmissionColor", _worldItemEmission * 2f);
        mat.EnableKeyword("_EMISSION");
        string matPath = $"{AssetFolder}/{safeName}_WorldItem_Mat.mat";
        AssetDatabase.CreateAsset(mat, matPath);
        visual.GetComponent<Renderer>().sharedMaterial = mat;

        // Physics
        var rb = worldItemGo.AddComponent<Rigidbody>();
        rb.mass = 0.5f;
        rb.isKinematic = true;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        var col = worldItemGo.AddComponent<BoxCollider>();
        col.size = Vector3.one * 0.35f;

        // Network
        worldItemGo.AddComponent<PurrNet.NetworkTransform>();

        // WeaponItem
        var weaponItem = worldItemGo.AddComponent<WeaponItem>();
        var wiSo = new SerializedObject(weaponItem);
        SetProperty(wiSo, "itemName", _spellName);
        SetProperty(wiSo, "weaponData", weaponData);
        SetProperty(wiSo, "rb", rb);
        wiSo.ApplyModifiedPropertiesWithoutUndo();

        string worldItemPath = $"{PrefabFolder}/{safeName}_WorldItem.prefab";
        var worldItemPrefab = PrefabUtility.SaveAsPrefabAsset(worldItemGo, worldItemPath);
        DestroyImmediate(worldItemGo);
        Debug.Log($"[SpellCreator] WorldItem: {worldItemPath}");

        // WeaponData.itemPrefab 연결
        var wdSo = new SerializedObject(weaponData);
        SetProperty(wdSo, "itemPrefab", worldItemPrefab);
        wdSo.ApplyModifiedProperties();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ──────────────────────────────────────
        // 5) weaponDatabase 등록
        // ──────────────────────────────────────
        bool dbRegistered = TryRegisterToWeaponDatabase(weaponData, _spellName);

        // ──────────────────────────────────────
        // 6) allItems 등록
        // ──────────────────────────────────────
        bool invRegistered = TryRegisterToInventoryManager(worldItemPrefab, _spellName);

        // ──────────────────────────────────────
        // Result
        // ──────────────────────────────────────
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"'{_spellName}' 스펠 생성 완료!");
        sb.AppendLine($"  SpellData: {spellDataPath}");
        sb.AppendLine($"  SpellItem: {spellItemPath}");
        sb.AppendLine($"  WeaponData: {weaponDataPath}");
        sb.AppendLine($"  WorldItem: {worldItemPath}");
        sb.AppendLine($"  weaponDatabase: {(dbRegistered ? "등록됨" : "수동 등록 필요")}");
        sb.AppendLine($"  allItems: {(invRegistered ? "등록됨" : "StartMap 열고 재실행 필요")}");
        _statusMessage = sb.ToString();
        _statusType = MessageType.Info;

        Debug.Log($"[SpellCreator] === {_spellName} 스펠 생성 완료! ===");

        // 생성된 에셋 선택
        Selection.activeObject = spellData;
        EditorGUIUtility.PingObject(spellData);
    }

    // ─── Registration Helpers ───

    private static bool TryRegisterToWeaponDatabase(WeaponData newWeaponData, string spellName)
    {
        string playerPrefabPath = "Assets/FPS/Cyber_Generic.prefab";
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefabPath);
        if (playerPrefab == null)
        {
            Debug.LogWarning($"[SpellCreator] Cyber_Generic.prefab을 찾을 수 없습니다.");
            return false;
        }

        var wm = playerPrefab.GetComponentInChildren<Demo.Scripts.Runtime.Character.FPSWeaponManager>(true);
        if (wm == null)
        {
            Debug.LogWarning($"[SpellCreator] FPSWeaponManager를 찾을 수 없습니다.");
            return false;
        }

        var wmSo = new SerializedObject(wm);
        var dbProp = wmSo.FindProperty("weaponDatabase");
        if (dbProp == null || !dbProp.isArray) return false;

        // 중복 확인 + NULL 슬롯 탐색
        int nullSlot = -1;
        for (int i = 0; i < dbProp.arraySize; i++)
        {
            var elem = dbProp.GetArrayElementAtIndex(i);
            if (elem.objectReferenceValue == newWeaponData)
            {
                Debug.Log($"[SpellCreator] {spellName}이 이미 weaponDatabase에 있습니다.");
                return true;
            }
            if (elem.objectReferenceValue == null && nullSlot < 0)
            {
                nullSlot = i;
            }
        }

        // NULL 슬롯이 있으면 재활용, 없으면 추가
        if (nullSlot >= 0)
        {
            dbProp.GetArrayElementAtIndex(nullSlot).objectReferenceValue = newWeaponData;
            Debug.Log($"[SpellCreator] {spellName}을 weaponDatabase[{nullSlot}] (빈 슬롯 재활용)에 등록!");
        }
        else
        {
            int newIndex = dbProp.arraySize;
            dbProp.arraySize++;
            dbProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = newWeaponData;
            Debug.Log($"[SpellCreator] {spellName}을 weaponDatabase[{newIndex}]에 등록!");
        }

        wmSo.ApplyModifiedProperties();
        PrefabUtility.SavePrefabAsset(playerPrefab);
        return true;
    }

    private static bool TryRegisterToInventoryManager(GameObject itemPrefab, string spellName)
    {
        var inventoryManager = Object.FindAnyObjectByType<InventoryManager>();
        if (inventoryManager == null)
        {
            Debug.LogWarning($"[SpellCreator] InventoryManager를 씬에서 찾을 수 없습니다. StartMap을 열고 다시 실행하세요.");
            return false;
        }

        var imSo = new SerializedObject(inventoryManager);
        var allItemsProp = imSo.FindProperty("allItems");
        if (allItemsProp == null || !allItemsProp.isArray) return false;

        var itemComponent = itemPrefab.GetComponent<Item>();
        if (itemComponent == null) return false;

        // 중복 확인
        for (int i = 0; i < allItemsProp.arraySize; i++)
        {
            if (allItemsProp.GetArrayElementAtIndex(i).objectReferenceValue == itemComponent)
            {
                Debug.Log($"[SpellCreator] {spellName}이 이미 allItems에 있습니다.");
                return true;
            }
        }

        int newIndex = allItemsProp.arraySize;
        allItemsProp.arraySize++;
        allItemsProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = itemComponent;
        imSo.ApplyModifiedProperties();

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Debug.Log($"[SpellCreator] {spellName}을 allItems[{newIndex}]에 등록!");
        return true;
    }

    // ─── Utility ───

    private static void SetProperty(SerializedObject so, string name, Object value)
    {
        var prop = so.FindProperty(name);
        if (prop != null) prop.objectReferenceValue = value;
    }

    private static void SetProperty(SerializedObject so, string name, string value)
    {
        var prop = so.FindProperty(name);
        if (prop != null) prop.stringValue = value;
    }

    private static LayerMask LayerMaskField(string label, LayerMask mask)
    {
        // Unity EditorGUI doesn't have a built-in LayerMask field, use workaround
        mask = EditorGUILayout.MaskField(label, mask,
            UnityEditorInternal.InternalEditorUtility.layers);
        return mask;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
