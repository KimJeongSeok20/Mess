using UnityEditor;
using UnityEngine;

public static class FixDrakeAmmoShopPrefab
{
    [MenuItem("Tools/Shop/Fix Drake Ammo Shop Prefab")]
    public static void Run()
    {
        const string sourcePath = "Assets/Items/Weapon/Drake_Mag.prefab";
        const string weaponDataPath = "Assets/Scripts/Inventory/Weapon/Data/Drake12_Data.asset";
        const string outputPath = "Assets/Scripts/Inventory/Weapon/Prefabs/Drake12_Ammo_Shop_Item.prefab";

        GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        WeaponData weaponData = AssetDatabase.LoadAssetAtPath<WeaponData>(weaponDataPath);
        if (sourcePrefab == null || weaponData == null)
        {
            Debug.LogError("[FixDrakeAmmoShopPrefab] Missing source prefab or weapon data.");
            return;
        }

        AmmoItem sourceAmmo = sourcePrefab.GetComponent<AmmoItem>();
        if (sourceAmmo == null)
        {
            Debug.LogError("[FixDrakeAmmoShopPrefab] Source prefab missing AmmoItem.");
            return;
        }

        GameObject instance = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
        if (instance == null)
        {
            Debug.LogError("[FixDrakeAmmoShopPrefab] Failed to instantiate source prefab.");
            return;
        }

        try
        {
            instance.name = "Drake12_Ammo_Shop_Item";

            AmmoItem existingAmmo = instance.GetComponent<AmmoItem>();
            if (existingAmmo != null)
                Object.DestroyImmediate(existingAmmo, true);

            WeaponShopItem existingShop = instance.GetComponent<WeaponShopItem>();
            if (existingShop != null)
                Object.DestroyImmediate(existingShop, true);

            if (instance.GetComponent<PurrNet.NetworkTransform>() == null)
                instance.AddComponent<PurrNet.NetworkTransform>();

            Rigidbody rigidbody = instance.GetComponent<Rigidbody>();
            WeaponShopItem shopItem = instance.AddComponent<WeaponShopItem>();

            SerializedObject sourceSerialized = new SerializedObject(sourceAmmo);
            SerializedObject targetSerialized = new SerializedObject(shopItem);

            targetSerialized.FindProperty("itemPicture").objectReferenceValue = sourceSerialized.FindProperty("itemPicture").objectReferenceValue;
            targetSerialized.FindProperty("itemName").stringValue = sourceSerialized.FindProperty("itemName").stringValue;
            targetSerialized.FindProperty("useRandomPrice").boolValue = true;
            targetSerialized.FindProperty("minPrice").intValue = 100;
            targetSerialized.FindProperty("maxPrice").intValue = 200;
            targetSerialized.FindProperty("applyOnAwake").boolValue = true;
            targetSerialized.FindProperty("rb").objectReferenceValue = rigidbody;
            targetSerialized.FindProperty("weaponData").objectReferenceValue = weaponData;
            targetSerialized.FindProperty("ammoItemPrefab").objectReferenceValue = sourcePrefab;
            targetSerialized.FindProperty("itemType").enumValueIndex = 1;
            targetSerialized.ApplyModifiedPropertiesWithoutUndo();

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(instance, outputPath);
            EditorUtility.SetDirty(savedPrefab);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        StoreAudioSetup.AssignStoreAudioToShopPrefabs();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[FixDrakeAmmoShopPrefab] Rebuilt Drake12_Ammo_Shop_Item from Drake_Mag source.");
    }
}
