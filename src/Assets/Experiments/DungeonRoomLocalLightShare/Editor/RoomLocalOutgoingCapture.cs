using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonRoomLocalLightShare.Editor
{
    public static class RoomLocalOutgoingCapture
    {
        public static string CaptureBothRooms()
        {
            string idle = RoomLocalEditorUtil.RequireIdleEditor();
            if (idle != null)
                return idle;

            string hash = RoomLocalHashGuard.CaptureOrVerify();
            if (hash.StartsWith("FAIL", StringComparison.Ordinal))
                return hash;

            try
            {
                RoomLocalEditorUtil.EnsureFolder(RoomLocalLightShareContract.OutgoingFolder);
                string start = CaptureRoom(
                    RoomLocalLightShareContract.StartRoomId,
                    RoomLocalLightShareContract.StartPrefabPath,
                    RoomLocalLightShareContract.StartOutgoingPath);
                string admin = CaptureRoom(
                    RoomLocalLightShareContract.AdministrativeRoomId,
                    RoomLocalLightShareContract.AdministrativePrefabPath,
                    RoomLocalLightShareContract.AdministrativeOutgoingPath);
                return "PASS outgoing portal capture (no Lightmapping.Bake)\n" + start + "\n" + admin;
            }
            catch (Exception exception)
            {
                return "FAIL outgoing capture: " + exception;
            }
        }

        private static string CaptureRoom(string roomId, string prefabPath, string assetPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                throw new InvalidOperationException("Missing prefab " + prefabPath);

            Scene temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, temp);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var switcher = instance.GetComponent<DungeonTileLightmapSwitcher>();
                if (switcher == null)
                    throw new InvalidOperationException(prefab.name + " has no DungeonTileLightmapSwitcher.");

                DungeonTileBakeData p100 = switcher.GetBakeData(DungeonTileLightmapSwitcher.PowerLevel.P100);
                DungeonTileBakeData p0 = switcher.GetBakeData(DungeonTileLightmapSwitcher.PowerLevel.P0);
                if (p100 == null || p0 == null)
                    throw new InvalidOperationException(prefab.name + " is missing P100/P0 bake data.");
                Transform doorway = instance.transform.Find(RoomLocalLightShareContract.PreferredDoorwayPath);
                if (doorway == null)
                    throw new InvalidOperationException(prefab.name + " missing doorway " +
                                                        RoomLocalLightShareContract.PreferredDoorwayPath);

                Texture2D hdr100 = RoomLocalPortalSampler.Capture(
                    instance.transform,
                    doorway,
                    p100,
                    RoomLocalLightShareContract.PortalWidth,
                    RoomLocalLightShareContract.PortalHeight);
                Texture2D hdr0 = RoomLocalPortalSampler.Capture(
                    instance.transform,
                    doorway,
                    p0,
                    RoomLocalLightShareContract.PortalWidth,
                    RoomLocalLightShareContract.PortalHeight);
                if (hdr100 == null || hdr0 == null)
                    throw new InvalidOperationException(prefab.name + " portal sample returned no texels.");

                string p100Path = WriteExr(assetPath, "P100", hdr100);
                string p0Path = WriteExr(assetPath, "P0", hdr0);
                Texture2D cookie100 = RoomLocalPortalSampler.CreateCookie(
                    hdr100, out float peak100);
                Texture2D cookie0 = RoomLocalPortalSampler.CreateCookie(
                    hdr0, out float peak0);
                string cookie100Path = WritePng(assetPath, "P100_Cookie", cookie100);
                string cookie0Path = WritePng(assetPath, "P0_Cookie", cookie0);

                OutgoingPortalMap map = AssetDatabase.LoadAssetAtPath<OutgoingPortalMap>(assetPath);
                if (map == null)
                {
                    map = ScriptableObject.CreateInstance<OutgoingPortalMap>();
                    AssetDatabase.CreateAsset(map, assetPath);
                }

                map.ConfigureAuthoring(
                    roomId,
                    RoomLocalLightShareContract.PreferredDoorwayPath,
                    AssetDatabase.LoadAssetAtPath<Texture2D>(p0Path),
                    AssetDatabase.LoadAssetAtPath<Texture2D>(p100Path),
                    AssetDatabase.LoadAssetAtPath<Texture2D>(cookie0Path),
                    AssetDatabase.LoadAssetAtPath<Texture2D>(cookie100Path),
                    RoomLocalPortalSampler.Average(hdr0),
                    RoomLocalPortalSampler.Average(hdr100),
                    peak0,
                    peak100);
                EditorUtility.SetDirty(map);
                UnityEngine.Object.DestroyImmediate(hdr100);
                UnityEngine.Object.DestroyImmediate(hdr0);
                UnityEngine.Object.DestroyImmediate(cookie100);
                UnityEngine.Object.DestroyImmediate(cookie0);
                AssetDatabase.SaveAssets();
                return roomId + " p0Intensity=" + map.Power0Intensity.ToString("0.000") +
                       " p100Intensity=" + map.Power100Intensity.ToString("0.000") +
                       " p0Peak=" + map.Power0Peak.ToString("0.000") +
                       " p100Peak=" + map.Power100Peak.ToString("0.000") +
                       " asset=" + assetPath;
            }
            finally
            {
                if (instance != null)
                    UnityEngine.Object.DestroyImmediate(instance);
                if (temp.IsValid() && temp.isLoaded)
                    EditorSceneManager.CloseScene(temp, true);
            }
        }

        private static string WriteExr(string assetPath, string suffix, Texture2D texture)
        {
            string path = Path.ChangeExtension(assetPath, null) + "_" + suffix + ".exr";
            File.WriteAllBytes(path, texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            AssetDatabase.ImportAsset(path);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = false;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return path;
        }

        private static string WritePng(string assetPath, string suffix, Texture2D texture)
        {
            string path = Path.ChangeExtension(assetPath, null) + "_" + suffix + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = false;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureType = TextureImporterType.Default;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return path;
        }
    }
}
