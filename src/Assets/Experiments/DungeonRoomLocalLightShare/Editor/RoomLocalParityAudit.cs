using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonRoomLocalLightShare.Editor
{
    public static class RoomLocalParityAudit
    {
        public static string AuditIsolatedScene()
        {
            string idle = RoomLocalEditorUtil.RequireIdleEditor();
            if (idle != null)
                return idle;

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() ||
                scene.path != RoomLocalLightShareContract.IsolatedScenePath)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                        RoomLocalLightShareContract.IsolatedScenePath) == null)
                    return "FAIL: isolated scene is missing. Run Build Isolated Start-Admin Scene first.";

                scene = EditorSceneManager.OpenScene(
                    RoomLocalLightShareContract.IsolatedScenePath,
                    OpenSceneMode.Single);
            }

            if (!scene.IsValid() || !scene.isLoaded ||
                scene.path != RoomLocalLightShareContract.IsolatedScenePath)
                return "FAIL: could not open " + RoomLocalLightShareContract.IsolatedScenePath;

            GameObject start = RoomLocalEditorUtil.FindRoot(
                scene, RoomLocalLightShareContract.StartRoomId);
            GameObject admin = RoomLocalEditorUtil.FindRoot(
                scene, RoomLocalLightShareContract.AdministrativeRoomId);
            if (start == null || admin == null)
                return "FAIL: isolated scene is missing Start or Admin roots.";

            int overlays = CountForbiddenOverlays(scene);
            string startReport = AuditAgainstPrefab(
                start,
                RoomLocalLightShareContract.StartPrefabPath);
            string adminReport = AuditAgainstPrefab(
                admin,
                RoomLocalLightShareContract.AdministrativePrefabPath);

            bool pass = overlays == 0 &&
                        startReport.StartsWith("PASS") &&
                        adminReport.StartsWith("PASS");
            return (pass ? "PASS" : "FAIL") + " isolated-scene parity\n" +
                   "forbiddenOverlayRenderers=" + overlays + "\n" +
                   startReport + "\n" +
                   adminReport;
        }

        private static int CountForbiddenOverlays(Scene scene)
        {
            int count = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                Renderer[] renderers = roots[r].GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null)
                        continue;
                    string name = renderers[i].name;
                    if (name.IndexOf("AdjacentPairOverlay", System.StringComparison.Ordinal) >= 0 ||
                        name.IndexOf("GiAdd", System.StringComparison.Ordinal) >= 0 ||
                        name.IndexOf("SpillReceiver", System.StringComparison.Ordinal) >= 0)
                        count++;
                }
            }

            return count;
        }

        private static string AuditAgainstPrefab(GameObject instance, string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                return "FAIL missing prefab " + prefabPath;

            Dictionary<string, Renderer> live = RoomLocalRendererKeys.BuildKeyMap(instance.transform);
            Dictionary<string, Renderer> canonical = RoomLocalRendererKeys.BuildKeyMap(prefab.transform);
            var missing = new List<string>();
            var materialMismatch = new List<string>();
            foreach (KeyValuePair<string, Renderer> pair in canonical)
            {
                if (!live.TryGetValue(pair.Key, out Renderer liveRenderer) || liveRenderer == null)
                {
                    missing.Add(pair.Key);
                    continue;
                }

                Material[] liveMats = liveRenderer.sharedMaterials;
                Material[] prefabMats = pair.Value.sharedMaterials;
                if (liveMats.Length != prefabMats.Length)
                {
                    materialMismatch.Add(pair.Key + " materialCount");
                    continue;
                }

                for (int i = 0; i < liveMats.Length; i++)
                {
                    if (liveMats[i] != prefabMats[i])
                        materialMismatch.Add(pair.Key + " slot " + i);
                }

                if (liveRenderer.sharedMaterial != null && pair.Value.sharedMaterial != null &&
                    liveRenderer.sharedMaterial.shader != pair.Value.sharedMaterial.shader)
                    materialMismatch.Add(pair.Key + " shader");
            }

            var builder = new StringBuilder();
            if (missing.Count == 0 && materialMismatch.Count == 0)
            {
                builder.Append("PASS ");
                builder.Append(instance.name);
                builder.Append(" renderers=");
                builder.Append(live.Count);
                builder.Append(" canonical=");
                builder.Append(canonical.Count);
                return builder.ToString();
            }

            builder.Append("FAIL ");
            builder.Append(instance.name);
            builder.Append(" missingKeys=");
            builder.Append(missing.Count);
            builder.Append(" materialMismatches=");
            builder.Append(materialMismatch.Count);
            return builder.ToString();
        }
    }
}
