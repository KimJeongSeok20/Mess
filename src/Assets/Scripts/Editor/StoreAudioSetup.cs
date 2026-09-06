using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

public static class StoreAudioSetup
{
    [MenuItem("Tools/Audio/Assign Store Audio To Shop Prefabs")]
    public static void AssignStoreAudioToShopPrefabs()
    {
        const string mixerPath = "Assets/SoundController.mixer";
        const string successPath = "Assets/Sfx/Buy_Success.mp3";
        const string failPath = "Assets/Sfx/Buy_Fail.mp3";

        var mixerAssets = AssetDatabase.LoadAllAssetsAtPath(mixerPath);
        AudioMixerGroup storeGroup = null;
        for (int i = 0; i < mixerAssets.Length; i++)
        {
            if (mixerAssets[i] is AudioMixerGroup group && group.name == "Store")
            {
                storeGroup = group;
                break;
            }
        }

        var successClip = AssetDatabase.LoadAssetAtPath<AudioClip>(successPath);
        var failClip = AssetDatabase.LoadAssetAtPath<AudioClip>(failPath);

        if (storeGroup == null)
        {
            Debug.LogError("[StoreAudioSetup] Store mixer group not found in Assets/SoundController.mixer");
            return;
        }

        if (successClip == null || failClip == null)
        {
            Debug.LogError("[StoreAudioSetup] Buy_Success or Buy_Fail clip not found.");
            return;
        }

        string[] roots =
        {
            "Assets/Scripts/Inventory/Weapon/Prefabs",
            "Assets/Scripts/InventoryShop/Backpack"
        };

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", roots);
        int updated = 0;

        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            ShopPurchaseItemBase shopItem = prefab.GetComponent<ShopPurchaseItemBase>();
            if (shopItem == null)
                continue;

            SerializedObject serialized = new SerializedObject(shopItem);
            serialized.FindProperty("buySuccessClip").objectReferenceValue = successClip;
            serialized.FindProperty("buyFailClip").objectReferenceValue = failClip;
            serialized.FindProperty("purchaseMixerGroup").objectReferenceValue = storeGroup;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(prefab);
            updated++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[StoreAudioSetup] Updated {updated} shop prefabs with Store mixer + buy clips.");
    }

    [MenuItem("Tools/Audio/Assign Store Mixer To Anvils")]
    public static void AssignStoreMixerToAnvils()
    {
        const string mixerPath = "Assets/SoundController.mixer";
        const string anvilPrefabPath = "Assets/Small Blacksmith Station/Prefab/Anvil Prefab.prefab";
        const string startMapScenePath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";

        AudioMixerGroup storeGroup = LoadStoreGroup(mixerPath);
        if (storeGroup == null)
        {
            Debug.LogError("[StoreAudioSetup] Store mixer group not found in Assets/SoundController.mixer");
            return;
        }

        int updated = 0;

        GameObject anvilPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(anvilPrefabPath);
        if (anvilPrefab != null)
        {
            updated += AssignStoreMixerToAnvilObject(anvilPrefab, storeGroup) ? 1 : 0;
            if (updated > 0)
                EditorUtility.SetDirty(anvilPrefab);
        }

        var scene = EditorSceneManager.OpenScene(startMapScenePath, OpenSceneMode.Single);
        foreach (AnvilInteraction anvil in Object.FindObjectsByType<AnvilInteraction>(FindObjectsSortMode.None))
        {
            if (AssignStoreMixerToAnvilObject(anvil.gameObject, storeGroup))
            {
                EditorUtility.SetDirty(anvil);
                if (anvil.TryGetComponent<AudioSource>(out var audioSource))
                    EditorUtility.SetDirty(audioSource);
                updated++;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[StoreAudioSetup] Updated {updated} anvil objects with Store mixer.");
    }

    private static AudioMixerGroup LoadStoreGroup(string mixerPath)
    {
        var mixerAssets = AssetDatabase.LoadAllAssetsAtPath(mixerPath);
        for (int i = 0; i < mixerAssets.Length; i++)
        {
            if (mixerAssets[i] is AudioMixerGroup group && group.name == "Store")
                return group;
        }

        return null;
    }

    private static bool AssignStoreMixerToAnvilObject(GameObject target, AudioMixerGroup storeGroup)
    {
        if (target == null || storeGroup == null)
            return false;

        AnvilInteraction anvil = target.GetComponent<AnvilInteraction>();
        if (anvil == null)
            return false;

        bool changed = false;

        SerializedObject serializedAnvil = new SerializedObject(anvil);
        SerializedProperty mixerProperty = serializedAnvil.FindProperty("upgradeMixerGroup");
        if (mixerProperty != null && mixerProperty.objectReferenceValue != storeGroup)
        {
            mixerProperty.objectReferenceValue = storeGroup;
            serializedAnvil.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }

        AudioSource audioSource = target.GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = target.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            changed = true;
        }

        if (audioSource.outputAudioMixerGroup != storeGroup)
        {
            audioSource.outputAudioMixerGroup = storeGroup;
            changed = true;
        }

        return changed;
    }
}
