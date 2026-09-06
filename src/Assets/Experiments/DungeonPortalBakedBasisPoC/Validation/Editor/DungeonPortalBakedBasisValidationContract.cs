using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DungeonPortalBakedBasisPoC;
using DungeonPortalBakedBasisPoC.Validation;
using DungeonPortalTransportPoC.KExactBasisV1.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonPortalBakedBasisPoC.Validation.Editor
{
    /// <summary>
    /// Pinned paths and static scene contracts for the isolated baked-basis validation path.
    /// It intentionally uses the same source-scene checksum and renderer-parity helper as the
    /// rejected-V21 recovery work, but owns only assets below DungeonPortalBakedBasisPoC.
    /// </summary>
    internal static class DungeonPortalBakedBasisValidationContract
    {
        internal const string OwnedRoot = "Assets/Experiments/DungeonPortalBakedBasisPoC";
        internal const string SceneFolder = OwnedRoot + "/Scenes";
        internal const string DataFolder = OwnedRoot + "/Data";
        internal const string EvidenceRoot = OwnedRoot + "/Evidence";
        internal const string BuiltScenePath =
            SceneFolder + "/Start_Admin_BakedBasisV1.unity";
        internal const string BuildManifestPath =
            DataFolder + "/build_manifest_DPBB_V1.txt";
        internal const string StartBasisPath =
            OwnedRoot + "/EndpointNativeGenerated/StartRoom_R000/StartRoom_R000_RoomBasis.asset";
        internal const string AdministrativeBasisPath =
            OwnedRoot + "/EndpointNativeGenerated/AdminstrativeSegregation_R000/" +
            "AdminstrativeSegregation_R000_RoomBasis.asset";
        internal const string EndpointNativeV2Root = OwnedRoot + "/EndpointNativeGeneratedV2";
        internal const string StartV2BasisPath =
            EndpointNativeV2Root + "/StartRoom_R000/StartRoom_R000_RoomBasis.asset";
        internal const string AdministrativeV2BasisPath =
            EndpointNativeV2Root + "/AdminstrativeSegregation_R000/" +
            "AdminstrativeSegregation_R000_RoomBasis.asset";
        internal const string CompositionShaderPath =
            OwnedRoot + "/Shaders/DungeonPortalBakedBasisCompose.shader";

        internal const string RuntimeRootName = "04_DungeonPortalBakedBasis_Runtime";
        internal const string DriverName = "Start_Admin_BakedBasisConnection";
        internal const string LiveDoorSourceName = "DoorAngleSource_Live";
        internal const string DoorShDriverName = "DoorSplitShDriver";
        internal const string ReflectionDriverName = "RoomReflections_P0Residual_P100";
        internal const string LegacyReflectionOwnerRootName = "DungeonPortalTransportPoC";
        internal const string LegacyReflectionDataFolder =
            "Assets/Experiments/DungeonPortalTransportPoC/Data/Reflection";
        internal const string StartToAdministrativeConnectionId =
            "Start_Admin_BakedBasisV1/StartToAdministrative";
        internal const string AdministrativeToStartConnectionId =
            "Start_Admin_BakedBasisV1/AdministrativeToStart";
        internal const string DoorwayId = "Doorways[5]/Door_SM_A[0]/DoorWayPoint[0]";

        internal const string SourceScenePath =
            "Assets/Experiments/DungeonPortalTransportPoC/Scenes/" +
            "Start_Admin_PortalTransportValidation.unity";
        internal const string ExpectedSourceSceneSha256 =
            "F087DD274822D9F8071CE8ADD0E261BC5314B2999EF4BDF3B1C1CB1B5DC4AF79";
        internal const string StartRoomId = "StartRoom_R000";
        internal const string AdministrativeRoomId = "AdminstrativeSegregation_R000";

        internal static readonly ReflectionSpec[] ReflectionSpecs =
        {
            new ReflectionSpec(
                "StartRoom_R000",
                "StartRoom_R000_Reflection",
                LegacyReflectionDataFolder + "/StartRoom_R000_ReflectionProfile.asset",
                true),
            new ReflectionSpec(
                "AdminstrativeSegregation_R000_S00",
                "AdminstrativeSegregation_R000_S00_Reflection",
                LegacyReflectionDataFolder +
                "/AdminstrativeSegregation_R000_S00_ReflectionProfile.asset",
                false),
            new ReflectionSpec(
                "AdminstrativeSegregation_R000_S01",
                "AdminstrativeSegregation_R000_S01_Reflection",
                LegacyReflectionDataFolder +
                "/AdminstrativeSegregation_R000_S01_ReflectionProfile.asset",
                false),
            new ReflectionSpec(
                "AdminstrativeSegregation_R000_S02",
                "AdminstrativeSegregation_R000_S02_Reflection",
                LegacyReflectionDataFolder +
                "/AdminstrativeSegregation_R000_S02_ReflectionProfile.asset",
                false),
            new ReflectionSpec(
                "AdminstrativeSegregation_R000_S03",
                "AdminstrativeSegregation_R000_S03_Reflection",
                LegacyReflectionDataFolder +
                "/AdminstrativeSegregation_R000_S03_ReflectionProfile.asset",
                false)
        };

        internal static Scene RequireCleanSourceSceneActive()
        {
            // Existing helper also validates the pinned raw source checksum, realtime manifest,
            // selected-light manifest, edit mode, and source cleanliness.
            return KExactBasisV1EditorContract.RequireCleanSourceSceneActive();
        }

        internal static void EnsureOwnedFolders()
        {
            EnsureFolder(OwnedRoot);
            EnsureFolder(SceneFolder);
            EnsureFolder(DataFolder);
            EnsureFolder(EvidenceRoot);
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            if (!AssetDatabase.IsValidFolder(OwnedRoot) ||
                !AssetDatabase.IsValidFolder(SceneFolder) ||
                !AssetDatabase.IsValidFolder(DataFolder) ||
                !AssetDatabase.IsValidFolder(EvidenceRoot))
            {
                throw new IOException("Unity did not register the isolated baked-basis folders.");
            }
        }

        internal static void RequireCleanBuiltSceneActiveInPlayMode()
        {
            if (!Application.isPlaying || !EditorApplication.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying)
            {
                throw new InvalidOperationException(
                    "An already-running stable Play Mode is required; this tool never changes Play Mode.");
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || Lightmapping.isRunning)
                throw new InvalidOperationException("Unity is compiling, updating, or lightmapping.");
            if (KExactBasisV1EditorContract.CountLoadedNonPreviewScenes() != 1 ||
                SceneManager.sceneCount != 1)
            {
                throw new InvalidOperationException("Exactly one non-preview scene is required.");
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded ||
                !string.Equals(NormalizePath(scene.path), BuiltScenePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The baked-basis validation scene must already be active in Play Mode.");
            }
        }

        internal static Dictionary<string, Renderer> BuildRendererKeyMap(Transform roomRoot)
        {
            if (roomRoot == null)
                throw new ArgumentNullException(nameof(roomRoot));
            Renderer[] traversal = roomRoot.GetComponentsInChildren<Renderer>(true);
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var result = new Dictionary<string, Renderer>(StringComparer.Ordinal);
            for (int i = 0; i < traversal.Length; i++)
            {
                Renderer renderer = traversal[i];
                if (renderer == null)
                    throw new InvalidOperationException("Renderer traversal contains null at " + i + ".");
                string barePath = GetBareRelativePath(roomRoot, renderer.transform);
                occurrences.TryGetValue(barePath, out int occurrence);
                occurrences[barePath] = occurrence + 1;
                string key = barePath + "#" + occurrence.ToString(CultureInfo.InvariantCulture);
                if (!result.TryAdd(key, renderer))
                    throw new InvalidOperationException("Duplicate canonical renderer key: " + key);
            }
            return result;
        }

        internal static string GetBareRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || (target != root && !target.IsChildOf(root)))
                throw new InvalidOperationException("Renderer transform is outside its room root.");
            if (target == root)
                return string.Empty;
            var parts = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                parts.Push(current.name);
                current = current.parent;
            }
            if (current != root)
                throw new InvalidOperationException("Renderer hierarchy escaped its room root.");
            return string.Join("/", parts);
        }

        internal static DungeonPortalBakedBasisConnectionDriver RequireDriver(Scene scene)
        {
            Transform runtimeRoot = FindUniqueDirectChild(
                KExactBasisV1EditorContract.FindUniqueRoot(scene,
                    KExactBasisV1EditorContract.ValidationRootName).transform,
                RuntimeRootName);
            DungeonPortalBakedBasisConnectionDriver[] drivers =
                runtimeRoot.GetComponentsInChildren<DungeonPortalBakedBasisConnectionDriver>(true);
            if (drivers.Length != 1 || drivers[0] == null)
                throw new InvalidOperationException("Expected exactly one baked-basis connection driver.");
            return drivers[0];
        }

        internal static DungeonPortalBakedBasisDoorShDriver RequireDoorShDriver(Scene scene)
        {
            Transform runtimeRoot = FindUniqueDirectChild(
                KExactBasisV1EditorContract.FindUniqueRoot(scene,
                    KExactBasisV1EditorContract.ValidationRootName).transform,
                RuntimeRootName);
            DungeonPortalBakedBasisDoorShDriver[] drivers =
                runtimeRoot.GetComponentsInChildren<DungeonPortalBakedBasisDoorShDriver>(true);
            if (drivers.Length != 1 || drivers[0] == null)
                throw new InvalidOperationException("Expected exactly one baked-basis door SH driver.");
            return drivers[0];
        }

        internal static DungeonPortalBakedBasisReflectionDriver RequireReflectionDriver(Scene scene)
        {
            Transform runtimeRoot = FindUniqueDirectChild(
                KExactBasisV1EditorContract.FindUniqueRoot(scene,
                    KExactBasisV1EditorContract.ValidationRootName).transform,
                RuntimeRootName);
            DungeonPortalBakedBasisReflectionDriver[] drivers =
                runtimeRoot.GetComponentsInChildren<DungeonPortalBakedBasisReflectionDriver>(true);
            if (drivers.Length != 1 || drivers[0] == null)
                throw new InvalidOperationException("Expected exactly one DPBB reflection driver.");
            return drivers[0];
        }

        internal static Transform FindUniqueDirectChild(Transform parent, string childName)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            Transform result = null;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform candidate = parent.GetChild(i);
                if (!string.Equals(candidate.name, childName, StringComparison.Ordinal))
                    continue;
                if (result != null)
                    throw new InvalidOperationException("Duplicate direct child: " + childName);
                result = candidate;
            }
            if (result == null)
                throw new InvalidOperationException("Missing direct child: " + childName);
            return result;
        }

        internal static string ComputeFileSha256(string assetPath)
        {
            string absolutePath = AssetPathToAbsolutePath(assetPath);
            if (!File.Exists(absolutePath))
                throw new FileNotFoundException("Required file does not exist.", absolutePath);
            using (FileStream stream = File.OpenRead(absolutePath))
            using (SHA256 sha = SHA256.Create())
                return ToHex(sha.ComputeHash(stream));
        }

        internal static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return ToHex(sha.ComputeHash(bytes));
        }

        internal static string AssetPathToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                (assetPath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar)));
        }

        internal static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        internal static void AssertBuiltSceneFileExists()
        {
            if (!File.Exists(AssetPathToAbsolutePath(BuiltScenePath)))
                throw new FileNotFoundException("Built baked-basis scene is missing.", BuiltScenePath);
        }

        private static void EnsureFolder(string assetPath)
        {
            Directory.CreateDirectory(AssetPathToAbsolutePath(assetPath));
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        internal readonly struct ReflectionSpec
        {
            internal readonly string StableId;
            internal readonly string LegacyProbeObjectName;
            internal readonly string ProfilePath;
            internal readonly bool UsesStartPower;

            internal ReflectionSpec(
                string stableId,
                string legacyProbeObjectName,
                string profilePath,
                bool usesStartPower)
            {
                StableId = stableId;
                LegacyProbeObjectName = legacyProbeObjectName;
                ProfilePath = profilePath;
                UsesStartPower = usesStartPower;
            }
        }
    }
}
