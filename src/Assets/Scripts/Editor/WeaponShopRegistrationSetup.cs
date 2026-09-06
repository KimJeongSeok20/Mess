using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WeaponShopRegistrationSetup
{
    private const string ScenePath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
    private const string ShopPrefabDirectory = "Assets/Scripts/Inventory/Weapon/Prefabs";

    private sealed class WeaponShopMapping
    {
        public string WeaponItemPath;
        public string AmmoItemPath;
        public string WeaponShopPath;
        public string AmmoShopPath;
    }

    [MenuItem("Tools/Shop/Register FPS Weapons To WeaponShop")]
    public static void RegisterFpsWeaponsToWeaponShop()
    {
        var mappings = new[]
        {
            new WeaponShopMapping
            {
                WeaponItemPath = "Assets/Items/Weapon/Drake-12_item.prefab",
                AmmoItemPath = "Assets/Items/Weapon/Drake_Mag.prefab",
                WeaponShopPath = ShopPrefabDirectory + "/Drake12_Shop_item.prefab",
                AmmoShopPath = ShopPrefabDirectory + "/Drake12_Ammo_Shop_Item.prefab"
            },
            new WeaponShopMapping
            {
                WeaponItemPath = "Assets/Items/Weapon/AK_item.prefab",
                AmmoItemPath = "Assets/Items/Weapon/AK_Mag.prefab",
                WeaponShopPath = ShopPrefabDirectory + "/AK_Shop_item.prefab",
                AmmoShopPath = ShopPrefabDirectory + "/AK_Ammo_Shop_Item.prefab"
            },
            new WeaponShopMapping
            {
                WeaponItemPath = "Assets/Items/Weapon/DGL50_item.prefab",
                AmmoItemPath = "Assets/Items/Weapon/DGL_Mag.prefab",
                WeaponShopPath = ShopPrefabDirectory + "/DGL50_Shop_item.prefab",
                AmmoShopPath = ShopPrefabDirectory + "/DGL50_Ammo_Shop_Item.prefab"
            },
            new WeaponShopMapping
            {
                WeaponItemPath = "Assets/Items/Weapon/Striker-V_item.prefab",
                AmmoItemPath = "Assets/Items/Weapon/Striker_Mag.prefab",
                WeaponShopPath = ShopPrefabDirectory + "/StrikerV_Shop_item.prefab",
                AmmoShopPath = ShopPrefabDirectory + "/StrikerV_Ammo_Shop_Item.prefab"
            },
            new WeaponShopMapping
            {
                WeaponItemPath = "Assets/Items/Weapon/RPG_item.prefab",
                AmmoItemPath = "Assets/Items/Weapon/RPG_Mag.prefab",
                WeaponShopPath = ShopPrefabDirectory + "/RPG_Shop_item.prefab",
                AmmoShopPath = ShopPrefabDirectory + "/RPG_Ammo_Shop_Item.prefab"
            }
        };

        foreach (var mapping in mappings)
        {
            CreateWeaponShopPrefab(mapping);
            CreateAmmoShopPrefab(mapping);
        }

        StoreAudioSetup.AssignStoreAudioToShopPrefabs();
        UpdateStartMapWeaponShop(mappings);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[WeaponShopRegistrationSetup] FPS weapon shop registration complete.");
    }

    private static void CreateWeaponShopPrefab(WeaponShopMapping mapping)
    {
        var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(mapping.WeaponItemPath);
        var sourceItem = sourcePrefab != null ? sourcePrefab.GetComponent<WeaponItem>() : null;
        if (sourcePrefab == null || sourceItem == null || sourceItem.WeaponData == null)
        {
            Debug.LogError($"[WeaponShopRegistrationSetup] Invalid weapon source prefab: {mapping.WeaponItemPath}");
            return;
        }

        CreateShopPrefab(mapping.WeaponShopPath, sourcePrefab, sourceItem, sourceItem.WeaponData, sourceItem.WeaponData.ammoItemPrefab, 0);
    }

    private static void CreateAmmoShopPrefab(WeaponShopMapping mapping)
    {
        var weaponSourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(mapping.WeaponItemPath);
        var weaponItem = weaponSourcePrefab != null ? weaponSourcePrefab.GetComponent<WeaponItem>() : null;
        var ammoSourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(mapping.AmmoItemPath);
        var ammoItem = ammoSourcePrefab != null ? ammoSourcePrefab.GetComponent<AmmoItem>() : null;
        if (weaponItem == null || weaponItem.WeaponData == null || ammoSourcePrefab == null || ammoItem == null)
        {
            Debug.LogError($"[WeaponShopRegistrationSetup] Invalid ammo source prefab: {mapping.AmmoItemPath}");
            return;
        }

        CreateShopPrefab(mapping.AmmoShopPath, ammoSourcePrefab, ammoItem, weaponItem.WeaponData, ammoSourcePrefab, 1);
    }

    private static void CreateShopPrefab(string outputPath, GameObject sourcePrefab, Item sourceItem, WeaponData weaponData, GameObject ammoItemPrefab, int itemType)
    {
        var instance = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
        if (instance == null)
        {
            Debug.LogError($"[WeaponShopRegistrationSetup] Failed to instantiate {sourcePrefab.name}");
            return;
        }

        try
        {
            instance.name = System.IO.Path.GetFileNameWithoutExtension(outputPath);

            var existingShopItem = instance.GetComponent<WeaponShopItem>();
            if (existingShopItem != null)
                Object.DestroyImmediate(existingShopItem, true);

            var existingWeaponItem = instance.GetComponent<WeaponItem>();
            if (existingWeaponItem != null)
                Object.DestroyImmediate(existingWeaponItem, true);

            var existingAmmoItem = instance.GetComponent<AmmoItem>();
            if (existingAmmoItem != null)
                Object.DestroyImmediate(existingAmmoItem, true);

            var shopItem = instance.AddComponent<WeaponShopItem>();
            CopyItemFields(sourceItem, shopItem, instance.GetComponent<Rigidbody>());

            var serialized = new SerializedObject(shopItem);
            serialized.FindProperty("weaponData").objectReferenceValue = weaponData;
            serialized.FindProperty("ammoItemPrefab").objectReferenceValue = ammoItemPrefab;
            serialized.FindProperty("itemType").enumValueIndex = itemType;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var savedPrefab = PrefabUtility.SaveAsPrefabAsset(instance, outputPath);
            EditorUtility.SetDirty(savedPrefab);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static void CopyItemFields(Item sourceItem, WeaponShopItem targetItem, Rigidbody rigidbody)
    {
        var sourceSerialized = new SerializedObject(sourceItem);
        var targetSerialized = new SerializedObject(targetItem);

        CopyObjectReference(sourceSerialized, targetSerialized, "itemPicture");
        CopyString(sourceSerialized, targetSerialized, "itemName");
        CopyBool(sourceSerialized, targetSerialized, "useRandomPrice");
        CopyInt(sourceSerialized, targetSerialized, "minPrice");
        CopyInt(sourceSerialized, targetSerialized, "maxPrice");
        CopyBool(sourceSerialized, targetSerialized, "applyOnAwake");

        var priceProp = sourceSerialized.FindProperty("price");
        var targetPriceProp = targetSerialized.FindProperty("price");
        if (priceProp != null && targetPriceProp != null)
        {
            targetPriceProp.FindPropertyRelative("_value").intValue = priceProp.FindPropertyRelative("_value").intValue;
            targetPriceProp.FindPropertyRelative("_ownerAuth").boolValue = priceProp.FindPropertyRelative("_ownerAuth").boolValue;
            targetPriceProp.FindPropertyRelative("_sendIntervalInSeconds").floatValue = priceProp.FindPropertyRelative("_sendIntervalInSeconds").floatValue;
        }

        targetSerialized.FindProperty("rb").objectReferenceValue = rigidbody;
        targetSerialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void UpdateStartMapWeaponShop(WeaponShopMapping[] mappings)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var weaponShop = Object.FindFirstObjectByType<WeaponShop>();
        if (weaponShop == null)
        {
            Debug.LogError("[WeaponShopRegistrationSetup] WeaponShop not found in StartMap.");
            return;
        }

        var shopPrefabs = new List<GameObject>(mappings.Length * 2);

        foreach (var mapping in mappings)
            shopPrefabs.Add(AssetDatabase.LoadAssetAtPath<GameObject>(mapping.WeaponShopPath));

        foreach (var mapping in mappings)
            shopPrefabs.Add(AssetDatabase.LoadAssetAtPath<GameObject>(mapping.AmmoShopPath));

        var serialized = new SerializedObject(weaponShop);
        SetObjectArray(serialized.FindProperty("shopPrefabs"), shopPrefabs);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(weaponShop.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void SetObjectArray(SerializedProperty property, List<GameObject> values)
    {
        property.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void CopyString(SerializedObject source, SerializedObject target, string propertyName)
    {
        target.FindProperty(propertyName).stringValue = source.FindProperty(propertyName).stringValue;
    }

    private static void CopyInt(SerializedObject source, SerializedObject target, string propertyName)
    {
        target.FindProperty(propertyName).intValue = source.FindProperty(propertyName).intValue;
    }

    private static void CopyBool(SerializedObject source, SerializedObject target, string propertyName)
    {
        target.FindProperty(propertyName).boolValue = source.FindProperty(propertyName).boolValue;
    }

    private static void CopyFloat(SerializedObject source, SerializedObject target, string propertyName)
    {
        target.FindProperty(propertyName).floatValue = source.FindProperty(propertyName).floatValue;
    }

    private static void CopyColor(SerializedObject source, SerializedObject target, string propertyName)
    {
        target.FindProperty(propertyName).colorValue = source.FindProperty(propertyName).colorValue;
    }

    private static void CopyObjectReference(SerializedObject source, SerializedObject target, string propertyName)
    {
        target.FindProperty(propertyName).objectReferenceValue = source.FindProperty(propertyName).objectReferenceValue;
    }
}
