using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonPortalTransportPoC.Editor
{
    /// <summary>
    /// Builds the first measured (K=1) direct-transport profile for each side of
    /// the isolated Start/Admin validation connection. The old adjacent-lighting
    /// assets are read-only radiance sources; no importer or production asset is edited.
    /// </summary>
    public static class DungeonPortalEndpointAuthoring
    {
        private const string ValidationScenePath =
            "Assets/Experiments/DungeonPortalTransportPoC/Scenes/Start_Admin_PortalTransportValidation.unity";
        private const string GeneratedFolder =
            "Assets/Experiments/DungeonPortalTransportPoC/Generated/EndpointProfiles";
        private const string ShaderPath =
            "Assets/Experiments/DungeonPortalTransportPoC/Shaders/PortalCookieDecode.shader";
        private const string DecodeShaderName =
            "Hidden/DungeonPortalTransportPoC/PortalCookieDecode";
        private const string CapturedRoot =
            "Assets/Experiments/DungeonAdjacentLightingPoC/Generated/Captured";
        private const string MeshRoot =
            "Assets/Experiments/DungeonAdjacentLightingPoC/Generated/Meshes";

        private const string StartRoomPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/StartRoom_R000.prefab";
        private const string AdminRoomPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/Tiles_Rotated/AdminstrativeSegregation_R000.prefab";
        private const string DoorPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/SomePicees/Door_SM_A_Door_Placement.prefab";

        private const string StartRoomObjectName = "StartRoom_R000_ProductionInstance";
        private const string AdminRoomObjectName =
            "AdminstrativeSegregation_R000_ProductionInstance";
        private const string DoorObjectName = "Door_SM_A_Door_Placement_ActiveSceneInstance";
        private const string StartEndpointObjectName = "StartRoom_Endpoint_ProfilePending";
        private const string AdminEndpointObjectName = "AdminRoom_Endpoint_ProfilePending";
        private const string ConnectionObjectName =
            "A_to_B_and_B_to_A_Connection_ProfileGate_DISABLED";
        private const string StableDoorwayId =
            "Doorways[5]/Door_SM_A[0]/DoorWayPoint[0]";

        private const int CookieResolution = 128;
        private const float SocketMinX = -0.5f;
        private const float SocketMaxX = 0.5f;
        private const float SocketMinY = 0f;
        private const float SocketMaxY = 2f;

        private static readonly EndpointSpec StartSpec = new EndpointSpec(
            "Start",
            "StartRoom_R000",
            StartRoomObjectName,
            StartRoomPrefabPath,
            StartEndpointObjectName,
            $"{CapturedRoot}/StartRoom_P100_LM1_Color.exr",
            "e55d7cc5159f93b4ba7b80cbafc4c2cb",
            $"{CapturedRoot}/StartRoom_P100_LM1_Direction.png",
            "35c70896eb223af4c999c1c3dfecea2e",
            $"{CapturedRoot}/StartRoom_P0_LM1_Color.exr",
            "408f334dea93b194a98266925bb55374",
            $"{CapturedRoot}/StartRoom_P0_LM1_Direction.png",
            "6c71d8430396c814286d9d3f733cd6f9",
            $"{MeshRoot}/StartRoom_f6b758291d94e2882187c8ac8bfcd1a1_PortalShellExtensionV6.asset",
            "64ed7bc31546dbd42bbde51cbb5a18f0",
            new Vector4(0.34093207f, 0.34093207f, -0.0013317659f, 0.41956666f),
            Vector3.right,
            $"{GeneratedFolder}/StartRoom_R000_PortalCookie.asset",
            $"{GeneratedFolder}/StartRoom_R000_EndpointProfile.asset");

        private static readonly EndpointSpec AdminSpec = new EndpointSpec(
            "Admin",
            "AdminstrativeSegregation_R000",
            AdminRoomObjectName,
            AdminRoomPrefabPath,
            AdminEndpointObjectName,
            $"{CapturedRoot}/AdminstrativeSegregation_P100_LM8_Color.exr",
            "53efe00cbe36ba947829d49004d3a9f2",
            $"{CapturedRoot}/AdminstrativeSegregation_P100_LM8_Direction.png",
            "59a6623b2818d3641a66984da246c58d",
            $"{CapturedRoot}/AdminstrativeSegregation_P0_LM8_Color.exr",
            "c33d93d38452ac24999d4a879082d823",
            $"{CapturedRoot}/AdminstrativeSegregation_P0_LM8_Direction.png",
            "f8fc313765a897f43a96d557dcc93b3c",
            $"{MeshRoot}/AdminstrativeSegregation_63571fef6843d8e4a8c2a5dc435754ce_PortalShellExtensionV6.asset",
            "07540fe85de78614e80da52067ef9342",
            new Vector4(0.349356f, 0.349356f, 0.34336188f, 0.345315f),
            Vector3.forward,
            $"{GeneratedFolder}/AdminstrativeSegregation_R000_PortalCookie.asset",
            $"{GeneratedFolder}/AdminstrativeSegregation_R000_EndpointProfile.asset");

        [MenuItem("Tools/Dungeon/Lighting/Portal Transport PoC/Author K=1 Endpoints")]
        public static void ApplyFromMenu()
        {
            string result = Apply();
            if (result.StartsWith("PASS", StringComparison.Ordinal))
                Debug.Log(result);
            else
                Debug.LogError(result);
        }

        public static void ApplyCli()
        {
            string result = Apply();
            if (!result.StartsWith("PASS", StringComparison.Ordinal))
                throw new InvalidOperationException(result);

            Debug.Log(result);
        }

        public static string Apply()
        {
            if (!TryValidateEditorState(out string stateFailure))
                return $"FAIL: {stateFailure}";

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ValidationScenePath) == null)
                return $"FAIL: build the isolated validation scene first: {ValidationScenePath}";

            Scene alreadyLoaded = SceneManager.GetSceneByPath(ValidationScenePath);
            if (alreadyLoaded.IsValid() && alreadyLoaded.isLoaded)
            {
                return
                    "FAIL: the generated validation scene is already loaded. Close it while " +
                    $"clean before endpoint authoring: {ValidationScenePath}";
            }

            Shader decodeShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (decodeShader == null || decodeShader.name != DecodeShaderName)
                return $"FAIL: missing or mismatched decode shader '{ShaderPath}'.";

            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
                return "FAIL: this editor GPU does not support the required temporary ARGBHalf target.";

            if (!PrefabGuard.TryCapture(StartRoomPrefabPath, out PrefabGuard startGuard, out string guardError) ||
                !PrefabGuard.TryCapture(AdminRoomPrefabPath, out PrefabGuard adminGuard, out guardError) ||
                !PrefabGuard.TryCapture(DoorPrefabPath, out PrefabGuard doorGuard, out guardError))
            {
                return $"FAIL: {guardError}";
            }

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene validationScene = default;
            Material decodeMaterial = null;
            GeneratedAssetTransaction assetTransaction = null;
            try
            {
                assetTransaction = GeneratedAssetTransaction.Capture(
                    StartSpec.CookieAssetPath,
                    AdminSpec.CookieAssetPath,
                    StartSpec.ProfileAssetPath,
                    AdminSpec.ProfileAssetPath);
                SourceData startSource = LoadSource(StartSpec);
                SourceData adminSource = LoadSource(AdminSpec);
                decodeMaterial = new Material(decodeShader) { hideFlags = HideFlags.HideAndDontSave };

                RadianceFit startFit = BuildRadianceFit(startSource, decodeMaterial);
                RadianceFit adminFit = BuildRadianceFit(adminSource, decodeMaterial);

                validationScene = EditorSceneManager.OpenScene(
                    ValidationScenePath,
                    OpenSceneMode.Additive);
                if (!validationScene.IsValid() || !validationScene.isLoaded)
                    throw new InvalidOperationException("Unity did not load the validation scene.");

                GameObject startRoom = FindUniqueSceneObject(validationScene, StartRoomObjectName);
                GameObject adminRoom = FindUniqueSceneObject(validationScene, AdminRoomObjectName);
                GameObject realDoor = FindUniqueSceneObject(validationScene, DoorObjectName);
                AssertPrefabSceneInstance(startRoom, StartRoomPrefabPath, "Start room");
                AssertPrefabSceneInstance(adminRoom, AdminRoomPrefabPath, "Admin room");
                AssertPrefabSceneInstance(realDoor, DoorPrefabPath, "real door");

                DungeonPortalEndpoint startEndpoint =
                    FindUniqueComponentOnNamedObject<DungeonPortalEndpoint>(
                        validationScene,
                        StartEndpointObjectName);
                DungeonPortalEndpoint adminEndpoint =
                    FindUniqueComponentOnNamedObject<DungeonPortalEndpoint>(
                        validationScene,
                        AdminEndpointObjectName);
                DungeonPortalTransportConnection connection =
                    FindUniqueComponentOnNamedObject<DungeonPortalTransportConnection>(
                        validationScene,
                        ConnectionObjectName);
                DungeonPortalPowerEnvelope startPower = RequireSingleSameObjectComponent<
                    DungeonPortalPowerEnvelope>(startEndpoint);
                DungeonPortalPowerEnvelope adminPower = RequireSingleSameObjectComponent<
                    DungeonPortalPowerEnvelope>(adminEndpoint);

                if (startEndpoint.DoorwayFrame == null || adminEndpoint.DoorwayFrame == null)
                    throw new InvalidOperationException("An endpoint has no authored doorway frame.");

                RendererGuard rendererGuard = RendererGuard.Capture(validationScene);
                Renderer[] startRenderers = GetProductionRoomRenderers(startRoom, realDoor);
                Renderer[] adminRenderers = GetProductionRoomRenderers(adminRoom, realDoor);
                Renderer[] doorRenderers = GetVisibleRenderers(realDoor);
                LightLayerUnion lightLayers = LightLayerUnion.From(
                    startRenderers,
                    adminRenderers,
                    doorRenderers);
                lightLayers.AssertCovers("Start room", startRenderers);
                lightLayers.AssertCovers("Admin room", adminRenderers);
                lightLayers.AssertCovers("real door", doorRenderers);

                EnsureAssetFolder(GeneratedFolder);
                Texture2D startCookie = CreateOrUpdateCookie(StartSpec, startFit.CookiePixels);
                Texture2D adminCookie = CreateOrUpdateCookie(AdminSpec, adminFit.CookiePixels);

                DungeonPortalEndpointProfile startProfile = CreateOrUpdateProfile(
                    StartSpec,
                    startSource,
                    startFit,
                    startCookie,
                    startEndpoint,
                    adminRenderers,
                    doorRenderers,
                    lightLayers);
                DungeonPortalEndpointProfile adminProfile = CreateOrUpdateProfile(
                    AdminSpec,
                    adminSource,
                    adminFit,
                    adminCookie,
                    adminEndpoint,
                    startRenderers,
                    doorRenderers,
                    lightLayers);

                startEndpoint.Configure(startEndpoint.DoorwayFrame, startProfile, startPower);
                adminEndpoint.Configure(adminEndpoint.DoorwayFrame, adminProfile, adminPower);
                // K=1 direct profiles are assigned, but runtime transport stays fail-closed
                // until an explicit validation pass approves enabling the connection.
                connection.enabled = false;
                EditorUtility.SetDirty(startEndpoint);
                EditorUtility.SetDirty(adminEndpoint);
                EditorUtility.SetDirty(connection);

                AssertAssignedProfile(startEndpoint, startProfile, "Start");
                AssertAssignedProfile(adminEndpoint, adminProfile, "Admin");
                if (connection.enabled)
                    throw new InvalidOperationException("Connection must remain disabled after bootstrap.");

                rendererGuard.AssertUnchanged();
                startGuard.AssertUnchanged();
                adminGuard.AssertUnchanged();
                doorGuard.AssertUnchanged();

                AssetDatabase.SaveAssetIfDirty(startCookie);
                AssetDatabase.SaveAssetIfDirty(adminCookie);
                AssetDatabase.SaveAssetIfDirty(startProfile);
                AssetDatabase.SaveAssetIfDirty(adminProfile);
                EditorSceneManager.MarkSceneDirty(validationScene);
                if (!EditorSceneManager.SaveScene(validationScene, ValidationScenePath, false))
                    throw new InvalidOperationException($"Unable to save '{ValidationScenePath}'.");

                assetTransaction.Commit();

                return
                    "PASS DungeonPortalTransportPoC K=1 endpoint authoring\n" +
                    $"scene={ValidationScenePath}\n" +
                    $"cookieResolution={CookieResolution}x{CookieResolution} linear RGBAHalf\n" +
                    $"startP0OverP100={startFit.Power0Factor:F6}\n" +
                    $"adminP0OverP100={adminFit.Power0Factor:F6}\n" +
                    $"cullingMask={lightLayers.CullingMask.value}\n" +
                    $"renderingLayerMask={unchecked((uint)lightLayers.RenderingLayerMask)}\n" +
                    "connectionEnabled=false incomingBounceLights=0\n" +
                    "rendererMaterialMpbWrites=0 productionPrefabAssetChanges=0";
            }
            catch (Exception exception)
            {
                string rollbackFailure = assetTransaction != null
                    ? assetTransaction.TryRollback()
                    : null;
                string rollbackSuffix = string.IsNullOrEmpty(rollbackFailure)
                    ? string.Empty
                    : $"\nGenerated-asset rollback also failed: {rollbackFailure}";
                return $"FAIL: K=1 endpoint authoring threw {exception}{rollbackSuffix}";
            }
            finally
            {
                if (decodeMaterial != null)
                    UnityEngine.Object.DestroyImmediate(decodeMaterial);

                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    EditorSceneManager.SetActiveScene(previousActiveScene);
                if (validationScene.IsValid() && validationScene.isLoaded)
                    EditorSceneManager.CloseScene(validationScene, true);
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    EditorSceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static bool TryValidateEditorState(out string failure)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                failure = "Unity is in or entering Play Mode; no asset or scene was touched.";
                return false;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                failure = "Unity is compiling or updating assets; no asset or scene was touched.";
                return false;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                {
                    failure =
                        $"loaded scene has unsaved changes: '{scene.name}' ({scene.path}). " +
                        "No asset or scene was touched.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private static SourceData LoadSource(EndpointSpec spec)
        {
            Texture2D p100Color = LoadExactAsset<Texture2D>(spec.Power100ColorPath, spec.Power100ColorGuid);
            Texture2D p100Direction = LoadExactAsset<Texture2D>(
                spec.Power100DirectionPath,
                spec.Power100DirectionGuid);
            Texture2D p0Color = LoadExactAsset<Texture2D>(spec.Power0ColorPath, spec.Power0ColorGuid);
            Texture2D p0Direction = LoadExactAsset<Texture2D>(
                spec.Power0DirectionPath,
                spec.Power0DirectionGuid);
            Mesh mesh = LoadExactAsset<Mesh>(spec.MeshPath, spec.MeshGuid);

            AssertTexturePair(spec.Label + " P100", p100Color, p100Direction);
            AssertTexturePair(spec.Label + " P0", p0Color, p0Direction);
            if (p100Color == p0Color || p100Direction == p0Direction)
                throw new InvalidOperationException($"{spec.Label} P0/P100 source assets are aliased.");
            if (!mesh.isReadable || mesh.vertexCount < 20)
                throw new InvalidOperationException($"{spec.Label} portal mesh must be readable with 20 vertices.");

            Vector3[] vertices = mesh.vertices;
            Vector2[] uv2 = mesh.uv2;
            if (uv2 == null || uv2.Length != vertices.Length)
                throw new InvalidOperationException($"{spec.Label} portal mesh has no complete UV2 channel.");

            PortalCardCorners corners = ResolvePortalCardCorners(spec.Label, vertices);
            Vector3 bottomLeft = vertices[corners.BottomLeft];
            Vector3 bottomRight = vertices[corners.BottomRight];
            Vector3 topRight = vertices[corners.TopRight];
            Vector3 topLeft = vertices[corners.TopLeft];
            AssertPortalCard(spec.Label, bottomLeft, bottomRight, topRight, topLeft);
            if (!IsFinite(spec.CapturedWorldNormal.x) ||
                !IsFinite(spec.CapturedWorldNormal.y) ||
                !IsFinite(spec.CapturedWorldNormal.z) ||
                Mathf.Abs(spec.CapturedWorldNormal.sqrMagnitude - 1f) > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"{spec.Label} captured portal-card world normal is not finite and unit length.");
            }

            Vector2 atlasBottomLeft = ToAtlasUv(
                BilerpCardUv(vertices, uv2, corners, SocketMinX, SocketMinY),
                spec.LightmapScaleOffset);
            Vector2 atlasBottomRight = ToAtlasUv(
                BilerpCardUv(vertices, uv2, corners, SocketMaxX, SocketMinY),
                spec.LightmapScaleOffset);
            Vector2 atlasTopRight = ToAtlasUv(
                BilerpCardUv(vertices, uv2, corners, SocketMaxX, SocketMaxY),
                spec.LightmapScaleOffset);
            Vector2 atlasTopLeft = ToAtlasUv(
                BilerpCardUv(vertices, uv2, corners, SocketMinX, SocketMaxY),
                spec.LightmapScaleOffset);
            AssertAtlasUv(spec.Label, atlasBottomLeft, atlasBottomRight, atlasTopRight, atlasTopLeft);

            float cardZ = (bottomLeft.z + bottomRight.z + topRight.z + topLeft.z) * 0.25f;
            return new SourceData(
                spec,
                p100Color,
                p100Direction,
                p0Color,
                p0Direction,
                atlasBottomLeft,
                atlasBottomRight,
                atlasTopRight,
                atlasTopLeft,
                new Vector3(
                    (SocketMinX + SocketMaxX) * 0.5f,
                    (SocketMinY + SocketMaxY) * 0.5f,
                    cardZ));
        }

        private static RadianceFit BuildRadianceFit(SourceData source, Material material)
        {
            Color[] p100 = DecodeCombinedDirectional(
                source,
                source.Power100Color,
                source.Power100Direction,
                material);
            Color[] p0 = DecodeCombinedDirectional(
                source,
                source.Power0Color,
                source.Power0Direction,
                material);

            var channelMaximum = Vector3.zero;
            double numerator = 0d;
            double denominator = 0d;
            double redNumerator = 0d;
            double greenNumerator = 0d;
            double blueNumerator = 0d;
            double redDenominator = 0d;
            double greenDenominator = 0d;
            double blueDenominator = 0d;
            for (int i = 0; i < p100.Length; i++)
            {
                AssertFiniteRadiance(source.Spec.Label + " P100", p100[i]);
                AssertFiniteRadiance(source.Spec.Label + " P0", p0[i]);
                channelMaximum.x = Mathf.Max(channelMaximum.x, p100[i].r);
                channelMaximum.y = Mathf.Max(channelMaximum.y, p100[i].g);
                channelMaximum.z = Mathf.Max(channelMaximum.z, p100[i].b);
                numerator += p0[i].r * p100[i].r +
                             p0[i].g * p100[i].g +
                             p0[i].b * p100[i].b;
                denominator += p100[i].r * p100[i].r +
                               p100[i].g * p100[i].g +
                               p100[i].b * p100[i].b;
                redNumerator += p0[i].r * p100[i].r;
                greenNumerator += p0[i].g * p100[i].g;
                blueNumerator += p0[i].b * p100[i].b;
                redDenominator += p100[i].r * p100[i].r;
                greenDenominator += p100[i].g * p100[i].g;
                blueDenominator += p100[i].b * p100[i].b;
            }

            float maximum = Mathf.Max(channelMaximum.x, channelMaximum.y, channelMaximum.z);
            if (maximum <= 0.000001f || denominator <= 0.000000000001d)
                throw new InvalidOperationException($"{source.Spec.Label} P100 socket crop is black.");

            float factor = Mathf.Clamp01((float)(numerator / denominator));
            Vector3 channelPower0Factor = new Vector3(
                ComputeNonNegativeFitFactor(redNumerator, redDenominator),
                ComputeNonNegativeFitFactor(greenNumerator, greenDenominator),
                ComputeNonNegativeFitFactor(blueNumerator, blueDenominator));
            Vector3 power0Representative = Vector3.Scale(
                channelMaximum,
                channelPower0Factor);
            float power0Maximum = Mathf.Max(
                power0Representative.x,
                power0Representative.y,
                power0Representative.z);
            Color[] normalized = new Color[p100.Length];
            for (int i = 0; i < p100.Length; i++)
            {
                normalized[i] = new Color(
                    NormalizeCookieChannel(p100[i].r, channelMaximum.x),
                    NormalizeCookieChannel(p100[i].g, channelMaximum.y),
                    NormalizeCookieChannel(p100[i].b, channelMaximum.z),
                    1f);
            }

            return new RadianceFit(
                normalized,
                new Color(
                    channelMaximum.x / maximum,
                    channelMaximum.y / maximum,
                    channelMaximum.z / maximum,
                    1f),
                power0Maximum > 0.000001f
                    ? new Color(
                        power0Representative.x / power0Maximum,
                        power0Representative.y / power0Maximum,
                        power0Representative.z / power0Maximum,
                        1f)
                    : Color.black,
                maximum,
                power0Maximum,
                factor);
        }

        private static float ComputeNonNegativeFitFactor(double numerator, double denominator)
        {
            return denominator > 0.000000000001d
                ? Mathf.Clamp01((float)(numerator / denominator))
                : 0f;
        }

        private static float NormalizeCookieChannel(float value, float channelMaximum)
        {
            return channelMaximum > 0.000001f
                ? Mathf.Clamp01(value / channelMaximum)
                : 0f;
        }

        private static Color[] DecodeCombinedDirectional(
            SourceData source,
            Texture2D color,
            Texture2D direction,
            Material material)
        {
            material.SetTexture("_LightmapColor", color);
            material.SetTexture("_LightmapDirection", direction);
            material.SetVector("_UvBottomLeft", ToShaderVector(source.AtlasBottomLeft));
            material.SetVector("_UvBottomRight", ToShaderVector(source.AtlasBottomRight));
            material.SetVector("_UvTopRight", ToShaderVector(source.AtlasTopRight));
            material.SetVector("_UvTopLeft", ToShaderVector(source.AtlasTopLeft));
            Vector3 capturedNormal = source.Spec.CapturedWorldNormal;
            material.SetVector(
                "_SurfaceNormal",
                new Vector4(capturedNormal.x, capturedNormal.y, capturedNormal.z, 0f));

            RenderTexture temporary = RenderTexture.GetTemporary(
                CookieResolution,
                CookieResolution,
                0,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Texture2D readback = new Texture2D(
                CookieResolution,
                CookieResolution,
                TextureFormat.RGBAHalf,
                false,
                true);
            try
            {
                Graphics.Blit(Texture2D.blackTexture, temporary, material, 0);
                RenderTexture.active = temporary;
                readback.ReadPixels(
                    new Rect(0f, 0f, CookieResolution, CookieResolution),
                    0,
                    0,
                    false);
                readback.Apply(false, false);
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(readback);
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static Texture2D CreateOrUpdateCookie(EndpointSpec spec, Color[] pixels)
        {
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(spec.CookieAssetPath);
            Texture2D cookie;
            if (existing == null)
            {
                cookie = new Texture2D(
                    CookieResolution,
                    CookieResolution,
                    TextureFormat.RGBAHalf,
                    false,
                    true)
                {
                    name = Path.GetFileNameWithoutExtension(spec.CookieAssetPath)
                };
                ConfigureCookie(cookie, pixels);
                AssetDatabase.CreateAsset(cookie, spec.CookieAssetPath);
            }
            else
            {
                cookie = existing as Texture2D;
                if (cookie == null)
                {
                    throw new InvalidOperationException(
                        $"Expected a Texture2D at '{spec.CookieAssetPath}', found {existing.GetType().Name}.");
                }

                if (cookie.width != CookieResolution || cookie.height != CookieResolution ||
                    cookie.format != TextureFormat.RGBAHalf)
                {
                    throw new InvalidOperationException(
                        $"Existing cookie '{spec.CookieAssetPath}' is not {CookieResolution}x" +
                        $"{CookieResolution} RGBAHalf; refusing to replace its asset identity.");
                }

                ConfigureCookie(cookie, pixels);
            }

            EditorUtility.SetDirty(cookie);
            return cookie;
        }

        private static void ConfigureCookie(Texture2D cookie, Color[] pixels)
        {
            cookie.wrapMode = TextureWrapMode.Clamp;
            cookie.filterMode = FilterMode.Bilinear;
            cookie.anisoLevel = 0;
            cookie.SetPixels(pixels);
            cookie.Apply(false, false);

            Color[] verified = cookie.GetPixels();
            for (int i = 0; i < verified.Length; i++)
            {
                AssertFiniteRadiance("generated cookie", verified[i]);
                if (verified[i].r > 1.0001f || verified[i].g > 1.0001f ||
                    verified[i].b > 1.0001f)
                {
                    throw new InvalidOperationException("Generated cookie RGB exceeds normalized range.");
                }
            }
        }

        private static DungeonPortalEndpointProfile CreateOrUpdateProfile(
            EndpointSpec spec,
            SourceData source,
            RadianceFit fit,
            Texture2D cookie,
            DungeonPortalEndpoint endpoint,
            Renderer[] receivingRoomRenderers,
            Renderer[] doorRenderers,
            LightLayerUnion layers)
        {
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(spec.ProfileAssetPath);
            DungeonPortalEndpointProfile profile;
            if (existing == null)
            {
                profile = ScriptableObject.CreateInstance<DungeonPortalEndpointProfile>();
                profile.name = Path.GetFileNameWithoutExtension(spec.ProfileAssetPath);
                AssetDatabase.CreateAsset(profile, spec.ProfileAssetPath);
            }
            else
            {
                profile = existing as DungeonPortalEndpointProfile;
                if (profile == null)
                {
                    throw new InvalidOperationException(
                        $"Expected a {nameof(DungeonPortalEndpointProfile)} at " +
                        $"'{spec.ProfileAssetPath}', found {existing.GetType().Name}.");
                }

            }

            float range = ComputeRange(
                endpoint.DoorwayFrame.TransformPoint(source.LocalLightPosition),
                receivingRoomRenderers,
                doorRenderers);
            float sourceDepth = Mathf.Abs(source.LocalLightPosition.z);
            float socketHalfDiagonal = new Vector2(
                (SocketMaxX - SocketMinX) * 0.5f,
                (SocketMaxY - SocketMinY) * 0.5f).magnitude;
            float spotAngle = Mathf.Clamp(
                2f * Mathf.Atan2(socketHalfDiagonal, Mathf.Max(0.01f, sourceDepth)) *
                Mathf.Rad2Deg,
                1f,
                179f);

            var descriptor = new DungeonPortalEndpointProfile.PortalDirectLightDescriptor
            {
                label = spec.Label + "_K1_OutgoingPortal",
                type = LightType.Spot,
                localPosition = source.LocalLightPosition,
                localEulerAngles = Vector3.zero,
                power0Color = fit.Power0Color,
                power100Color = fit.Power100Color,
                power0Intensity = fit.Power0Scale,
                power100Intensity = fit.Power100Scale,
                range = range,
                spotAngle = spotAngle,
                innerSpotAngle = spotAngle,
                cookie = cookie,
                castShadows = true,
                shadowStrength = 1f,
                cullingMask = layers.CullingMask,
                renderingLayerMask = layers.RenderingLayerMask
            };
            profile.ConfigureAuthoring(
                spec.RoomId,
                StableDoorwayId,
                new[] { descriptor },
                Array.Empty<DungeonPortalEndpointProfile.PortalBounceLightDescriptor>());
            if (!profile.TryValidate(out string validationError))
                throw new InvalidOperationException($"{spec.Label} endpoint profile: {validationError}");
            if (profile.IncomingBounceLights.Length != 0)
                throw new InvalidOperationException($"{spec.Label} bootstrap invented bounce lights.");

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static float ComputeRange(
            Vector3 lightPosition,
            Renderer[] roomRenderers,
            Renderer[] doorRenderers)
        {
            float farthestSquared = 0f;
            AccumulateFarthestBoundsDistance(lightPosition, roomRenderers, ref farthestSquared);
            AccumulateFarthestBoundsDistance(lightPosition, doorRenderers, ref farthestSquared);
            if (farthestSquared <= 0f)
                throw new InvalidOperationException("Unable to derive proxy-light range from renderers.");
            return Mathf.Sqrt(farthestSquared) + 0.01f;
        }

        private static void AccumulateFarthestBoundsDistance(
            Vector3 position,
            Renderer[] renderers,
            ref float farthestSquared)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Bounds bounds = renderers[i].bounds;
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    farthestSquared = Mathf.Max(
                        farthestSquared,
                        (point - position).sqrMagnitude);
                }
            }
        }

        private static T LoadExactAsset<T>(string path, string expectedGuid)
            where T : UnityEngine.Object
        {
            string actualGuid = AssetDatabase.AssetPathToGUID(path);
            if (!string.Equals(actualGuid, expectedGuid, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Source GUID mismatch for '{path}'. expected={expectedGuid} actual={actualGuid}.");
            }

            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException($"Missing or wrong-type source asset '{path}'.");
            if (EditorUtility.IsDirty(asset))
                throw new InvalidOperationException($"Read-only source asset is already dirty: '{path}'.");
            return asset;
        }

        private static void AssertTexturePair(string label, Texture2D color, Texture2D direction)
        {
            if (color.width <= 0 || color.height <= 0 ||
                color.width != direction.width || color.height != direction.height)
            {
                throw new InvalidOperationException($"{label} color/direction dimensions do not match.");
            }
        }

        private static void AssertPortalCard(
            string label,
            Vector3 bottomLeft,
            Vector3 bottomRight,
            Vector3 topRight,
            Vector3 topLeft)
        {
            const float epsilon = 0.0001f;
            if (Mathf.Abs(bottomLeft.y - bottomRight.y) > epsilon ||
                Mathf.Abs(topLeft.y - topRight.y) > epsilon ||
                Mathf.Abs(bottomLeft.x - topLeft.x) > epsilon ||
                Mathf.Abs(bottomRight.x - topRight.x) > epsilon ||
                Mathf.Max(bottomLeft.z, bottomRight.z, topRight.z, topLeft.z) -
                Mathf.Min(bottomLeft.z, bottomRight.z, topRight.z, topLeft.z) > epsilon ||
                Mathf.Abs((bottomLeft.z + bottomRight.z + topRight.z + topLeft.z) * 0.25f +
                          0.5f) > epsilon ||
                bottomLeft.x > SocketMinX || bottomRight.x < SocketMaxX ||
                bottomLeft.y > SocketMinY + 0.05f || topLeft.y < SocketMaxY)
            {
                throw new InvalidOperationException(
                    $"{label} mesh vertices 16..19 no longer define the expected source-side portal card.");
            }
        }

        private static Vector2 BilerpCardUv(
            Vector3[] vertices,
            Vector2[] uv2,
            PortalCardCorners corners,
            float localX,
            float localY)
        {
            Vector3 bottomLeft = vertices[corners.BottomLeft];
            Vector3 bottomRight = vertices[corners.BottomRight];
            Vector3 topLeft = vertices[corners.TopLeft];
            float x = (localX - bottomLeft.x) / (bottomRight.x - bottomLeft.x);
            float y = (localY - bottomLeft.y) / (topLeft.y - bottomLeft.y);
            Vector2 lower = Vector2.LerpUnclamped(
                uv2[corners.BottomLeft],
                uv2[corners.BottomRight],
                x);
            Vector2 upper = Vector2.LerpUnclamped(
                uv2[corners.TopLeft],
                uv2[corners.TopRight],
                x);
            return Vector2.LerpUnclamped(lower, upper, y);
        }

        private static PortalCardCorners ResolvePortalCardCorners(
            string label,
            Vector3[] vertices)
        {
            const int firstCardVertex = 16;
            const int cardVertexCount = 4;
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = firstCardVertex; i < firstCardVertex + cardVertexCount; i++)
            {
                minX = Mathf.Min(minX, vertices[i].x);
                maxX = Mathf.Max(maxX, vertices[i].x);
                minY = Mathf.Min(minY, vertices[i].y);
                maxY = Mathf.Max(maxY, vertices[i].y);
            }

            int bottomLeft = FindUniquePortalCardCorner(
                label, vertices, minX, minY, "bottom-left");
            int bottomRight = FindUniquePortalCardCorner(
                label, vertices, maxX, minY, "bottom-right");
            int topRight = FindUniquePortalCardCorner(
                label, vertices, maxX, maxY, "top-right");
            int topLeft = FindUniquePortalCardCorner(
                label, vertices, minX, maxY, "top-left");
            return new PortalCardCorners(bottomLeft, bottomRight, topRight, topLeft);
        }

        private static int FindUniquePortalCardCorner(
            string label,
            Vector3[] vertices,
            float targetX,
            float targetY,
            string cornerLabel)
        {
            const int firstCardVertex = 16;
            const int cardVertexCount = 4;
            const float epsilon = 0.0001f;
            int found = -1;
            for (int i = firstCardVertex; i < firstCardVertex + cardVertexCount; i++)
            {
                if (Mathf.Abs(vertices[i].x - targetX) > epsilon ||
                    Mathf.Abs(vertices[i].y - targetY) > epsilon)
                {
                    continue;
                }

                if (found >= 0)
                {
                    throw new InvalidOperationException(
                        $"{label} portal card has duplicate {cornerLabel} vertices.");
                }

                found = i;
            }

            if (found < 0)
            {
                throw new InvalidOperationException(
                    $"{label} portal card has no unique {cornerLabel} vertex.");
            }

            return found;
        }

        private static Vector2 ToAtlasUv(Vector2 meshUv2, Vector4 scaleOffset)
        {
            return new Vector2(
                meshUv2.x * scaleOffset.x + scaleOffset.z,
                meshUv2.y * scaleOffset.y + scaleOffset.w);
        }

        private static Vector4 ToShaderVector(Vector2 value)
        {
            return new Vector4(value.x, value.y, 0f, 0f);
        }

        private static void AssertAtlasUv(string label, params Vector2[] corners)
        {
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 uv = corners[i];
                if (!IsFinite(uv.x) || !IsFinite(uv.y) ||
                    uv.x < -0.001f || uv.x > 1.001f ||
                    uv.y < -0.001f || uv.y > 1.001f)
                {
                    throw new InvalidOperationException($"{label} socket crop leaves the source atlas.");
                }
            }
        }

        private static void AssertFiniteRadiance(string label, Color value)
        {
            if (!IsFinite(value.r) || !IsFinite(value.g) || !IsFinite(value.b) ||
                value.r < 0f || value.g < 0f || value.b < 0f)
            {
                throw new InvalidOperationException($"{label} contains invalid decoded radiance.");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static Renderer[] GetProductionRoomRenderers(GameObject room, GameObject door)
        {
            Renderer[] all = room.GetComponentsInChildren<Renderer>(true);
            var production = new List<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer renderer = all[i];
                if (renderer.transform.IsChildOf(door.transform))
                    continue;
                production.Add(renderer);
            }

            if (production.Count == 0)
                throw new InvalidOperationException($"'{room.name}' has no production renderers.");
            return production.ToArray();
        }

        private static Renderer[] GetVisibleRenderers(GameObject root)
        {
            Renderer[] all = root.GetComponentsInChildren<Renderer>(true);
            var visible = new List<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                if (IsVisible(all[i]))
                    visible.Add(all[i]);
            }

            if (visible.Count == 0)
                throw new InvalidOperationException($"'{root.name}' has no visible renderers.");
            return visible.ToArray();
        }

        private static bool IsVisible(Renderer renderer)
        {
            return renderer != null && renderer.enabled &&
                   !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy;
        }

        private static GameObject FindUniqueSceneObject(Scene scene, string name)
        {
            GameObject result = null;
            int count = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (transforms[i].name != name)
                        continue;
                    result = transforms[i].gameObject;
                    count++;
                }
            }

            if (count != 1)
                throw new InvalidOperationException($"Expected one scene object '{name}', found {count}.");
            return result;
        }

        private static T FindUniqueComponentOnNamedObject<T>(Scene scene, string name)
            where T : Component
        {
            GameObject owner = FindUniqueSceneObject(scene, name);
            T[] components = owner.GetComponents<T>();
            if (components.Length != 1)
                throw new InvalidOperationException($"Expected one {typeof(T).Name} on '{name}'.");
            return components[0];
        }

        private static T RequireSingleSameObjectComponent<T>(Component owner)
            where T : Component
        {
            T[] components = owner.GetComponents<T>();
            if (components.Length != 1)
                throw new InvalidOperationException($"Expected one {typeof(T).Name} on '{owner.name}'.");
            return components[0];
        }

        private static void AssertPrefabSceneInstance(
            GameObject instance,
            string expectedPrefabPath,
            string label)
        {
            if (instance.scene.path != ValidationScenePath ||
                PrefabUtility.IsPartOfPrefabAsset(instance))
            {
                throw new InvalidOperationException($"{label} is not an isolated scene instance.");
            }

            string actual = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance);
            if (actual != expectedPrefabPath)
            {
                throw new InvalidOperationException(
                    $"{label} source prefab mismatch. expected='{expectedPrefabPath}' actual='{actual}'.");
            }
        }

        private static void AssertAssignedProfile(
            DungeonPortalEndpoint endpoint,
            DungeonPortalEndpointProfile profile,
            string label)
        {
            if (endpoint.Profile != profile)
                throw new InvalidOperationException($"{label} endpoint profile was not assigned.");
            if (!profile.TryValidate(out string error))
                throw new InvalidOperationException($"{label} endpoint assignment is invalid: {error}");
            if (profile.IncomingBounceLights.Length != 0)
                throw new InvalidOperationException($"{label} endpoint contains invented bounce lights.");
        }

        private static void EnsureAssetFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            int separator = folder.LastIndexOf('/');
            if (separator <= 0 || separator >= folder.Length - 1)
                throw new InvalidOperationException($"Invalid asset folder '{folder}'.");
            string parent = folder.Substring(0, separator);
            EnsureAssetFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, folder.Substring(separator + 1))))
                throw new InvalidOperationException($"Unable to create asset folder '{folder}'.");
        }

        private sealed class EndpointSpec
        {
            public EndpointSpec(
                string label,
                string roomId,
                string roomObjectName,
                string roomPrefabPath,
                string endpointObjectName,
                string power100ColorPath,
                string power100ColorGuid,
                string power100DirectionPath,
                string power100DirectionGuid,
                string power0ColorPath,
                string power0ColorGuid,
                string power0DirectionPath,
                string power0DirectionGuid,
                string meshPath,
                string meshGuid,
                Vector4 lightmapScaleOffset,
                Vector3 capturedWorldNormal,
                string cookieAssetPath,
                string profileAssetPath)
            {
                Label = label;
                RoomId = roomId;
                RoomObjectName = roomObjectName;
                RoomPrefabPath = roomPrefabPath;
                EndpointObjectName = endpointObjectName;
                Power100ColorPath = power100ColorPath;
                Power100ColorGuid = power100ColorGuid;
                Power100DirectionPath = power100DirectionPath;
                Power100DirectionGuid = power100DirectionGuid;
                Power0ColorPath = power0ColorPath;
                Power0ColorGuid = power0ColorGuid;
                Power0DirectionPath = power0DirectionPath;
                Power0DirectionGuid = power0DirectionGuid;
                MeshPath = meshPath;
                MeshGuid = meshGuid;
                LightmapScaleOffset = lightmapScaleOffset;
                CapturedWorldNormal = capturedWorldNormal;
                CookieAssetPath = cookieAssetPath;
                ProfileAssetPath = profileAssetPath;
            }

            public string Label { get; }
            public string RoomId { get; }
            public string RoomObjectName { get; }
            public string RoomPrefabPath { get; }
            public string EndpointObjectName { get; }
            public string Power100ColorPath { get; }
            public string Power100ColorGuid { get; }
            public string Power100DirectionPath { get; }
            public string Power100DirectionGuid { get; }
            public string Power0ColorPath { get; }
            public string Power0ColorGuid { get; }
            public string Power0DirectionPath { get; }
            public string Power0DirectionGuid { get; }
            public string MeshPath { get; }
            public string MeshGuid { get; }
            public Vector4 LightmapScaleOffset { get; }
            public Vector3 CapturedWorldNormal { get; }
            public string CookieAssetPath { get; }
            public string ProfileAssetPath { get; }
        }

        private sealed class SourceData
        {
            public SourceData(
                EndpointSpec spec,
                Texture2D power100Color,
                Texture2D power100Direction,
                Texture2D power0Color,
                Texture2D power0Direction,
                Vector2 atlasBottomLeft,
                Vector2 atlasBottomRight,
                Vector2 atlasTopRight,
                Vector2 atlasTopLeft,
                Vector3 localLightPosition)
            {
                Spec = spec;
                Power100Color = power100Color;
                Power100Direction = power100Direction;
                Power0Color = power0Color;
                Power0Direction = power0Direction;
                AtlasBottomLeft = atlasBottomLeft;
                AtlasBottomRight = atlasBottomRight;
                AtlasTopRight = atlasTopRight;
                AtlasTopLeft = atlasTopLeft;
                LocalLightPosition = localLightPosition;
            }

            public EndpointSpec Spec { get; }
            public Texture2D Power100Color { get; }
            public Texture2D Power100Direction { get; }
            public Texture2D Power0Color { get; }
            public Texture2D Power0Direction { get; }
            public Vector2 AtlasBottomLeft { get; }
            public Vector2 AtlasBottomRight { get; }
            public Vector2 AtlasTopRight { get; }
            public Vector2 AtlasTopLeft { get; }
            public Vector3 LocalLightPosition { get; }
        }

        private readonly struct PortalCardCorners
        {
            public PortalCardCorners(
                int bottomLeft,
                int bottomRight,
                int topRight,
                int topLeft)
            {
                BottomLeft = bottomLeft;
                BottomRight = bottomRight;
                TopRight = topRight;
                TopLeft = topLeft;
            }

            public int BottomLeft { get; }
            public int BottomRight { get; }
            public int TopRight { get; }
            public int TopLeft { get; }
        }

        private readonly struct RadianceFit
        {
            public RadianceFit(
                Color[] cookiePixels,
                Color power100Color,
                Color power0Color,
                float power100Scale,
                float power0Scale,
                float power0Factor)
            {
                CookiePixels = cookiePixels;
                Power100Color = power100Color;
                Power0Color = power0Color;
                Power100Scale = power100Scale;
                Power0Scale = power0Scale;
                Power0Factor = power0Factor;
            }

            public Color[] CookiePixels { get; }
            public Color Power100Color { get; }
            public Color Power0Color { get; }
            public float Power100Scale { get; }
            public float Power0Scale { get; }
            public float Power0Factor { get; }
        }

        private readonly struct LightLayerUnion
        {
            private LightLayerUnion(LayerMask cullingMask, int renderingLayerMask)
            {
                CullingMask = cullingMask;
                RenderingLayerMask = renderingLayerMask;
            }

            public LayerMask CullingMask { get; }
            public int RenderingLayerMask { get; }

            public static LightLayerUnion From(params Renderer[][] groups)
            {
                int culling = 0;
                uint rendering = 0u;
                for (int group = 0; group < groups.Length; group++)
                {
                    Renderer[] renderers = groups[group];
                    if (renderers == null || renderers.Length == 0)
                        throw new InvalidOperationException("A required renderer group is empty.");
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        int layer = renderers[i].gameObject.layer;
                        if (layer < 0 || layer > 31 || renderers[i].renderingLayerMask == 0u)
                            throw new InvalidOperationException("A renderer has an invalid light-layer binding.");
                        culling |= 1 << layer;
                        rendering |= renderers[i].renderingLayerMask;
                    }
                }

                if (culling == 0 || rendering == 0u)
                    throw new InvalidOperationException("Computed proxy-light masks are empty.");
                return new LightLayerUnion(culling, unchecked((int)rendering));
            }

            public void AssertCovers(string label, Renderer[] renderers)
            {
                uint rendering = unchecked((uint)RenderingLayerMask);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    int layerBit = 1 << renderer.gameObject.layer;
                    if ((CullingMask.value & layerBit) == 0 ||
                        (rendering & renderer.renderingLayerMask) == 0u)
                    {
                        throw new InvalidOperationException(
                            $"Computed masks do not cover {label} renderer '{renderer.name}'.");
                    }
                }
            }
        }

        private sealed class RendererGuard
        {
            private readonly RendererState[] states;

            private RendererGuard(RendererState[] states)
            {
                this.states = states;
            }

            public static RendererGuard Capture(Scene scene)
            {
                var renderers = new List<Renderer>();
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                    renderers.AddRange(roots[i].GetComponentsInChildren<Renderer>(true));
                var states = new RendererState[renderers.Count];
                for (int i = 0; i < renderers.Count; i++)
                    states[i] = new RendererState(renderers[i]);
                return new RendererGuard(states);
            }

            public void AssertUnchanged()
            {
                for (int i = 0; i < states.Length; i++)
                    states[i].AssertUnchanged();
            }
        }

        private sealed class RendererState
        {
            private readonly Renderer renderer;
            private readonly string serializedState;
            private readonly bool hadPropertyBlock;
            private readonly Material[] sharedMaterials;
            private readonly string[] materialStates;
            private readonly Hash128[] materialHashes;

            public RendererState(Renderer renderer)
            {
                this.renderer = renderer;
                serializedState = EditorJsonUtility.ToJson(renderer, false);
                hadPropertyBlock = renderer.HasPropertyBlock();
                sharedMaterials = renderer.sharedMaterials;
                materialStates = new string[sharedMaterials.Length];
                materialHashes = new Hash128[sharedMaterials.Length];
                for (int i = 0; i < sharedMaterials.Length; i++)
                {
                    Material material = sharedMaterials[i];
                    if (material == null)
                        continue;
                    materialStates[i] = EditorJsonUtility.ToJson(material, false);
                    string path = AssetDatabase.GetAssetPath(material);
                    if (!string.IsNullOrEmpty(path))
                        materialHashes[i] = AssetDatabase.GetAssetDependencyHash(path);
                }
            }

            public void AssertUnchanged()
            {
                if (renderer == null ||
                    serializedState != EditorJsonUtility.ToJson(renderer, false) ||
                    hadPropertyBlock != renderer.HasPropertyBlock())
                {
                    throw new InvalidOperationException("Renderer or MPB presence changed during authoring.");
                }

                Material[] current = renderer.sharedMaterials;
                if (current.Length != sharedMaterials.Length)
                    throw new InvalidOperationException("Renderer material slots changed during authoring.");
                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i] != sharedMaterials[i])
                        throw new InvalidOperationException("Renderer material reference changed during authoring.");
                    Material material = current[i];
                    if (material == null)
                        continue;
                    string path = AssetDatabase.GetAssetPath(material);
                    Hash128 currentHash = string.IsNullOrEmpty(path)
                        ? default
                        : AssetDatabase.GetAssetDependencyHash(path);
                    if (materialStates[i] != EditorJsonUtility.ToJson(material, false) ||
                        currentHash != materialHashes[i])
                    {
                        throw new InvalidOperationException("Production material changed during authoring.");
                    }
                }
            }
        }

        private sealed class GeneratedAssetTransaction
        {
            private readonly GeneratedAssetSnapshot[] snapshots;
            private bool committed;

            private GeneratedAssetTransaction(GeneratedAssetSnapshot[] snapshots)
            {
                this.snapshots = snapshots;
            }

            public static GeneratedAssetTransaction Capture(params string[] assetPaths)
            {
                if (assetPaths == null || assetPaths.Length == 0)
                    throw new InvalidOperationException("No generated assets were supplied for rollback capture.");

                var snapshots = new GeneratedAssetSnapshot[assetPaths.Length];
                for (int i = 0; i < assetPaths.Length; i++)
                    snapshots[i] = GeneratedAssetSnapshot.Capture(assetPaths[i]);
                return new GeneratedAssetTransaction(snapshots);
            }

            public void Commit()
            {
                committed = true;
            }

            public string TryRollback()
            {
                if (committed)
                    return null;

                var failures = new List<string>();
                for (int i = snapshots.Length - 1; i >= 0; i--)
                {
                    try
                    {
                        snapshots[i].Restore();
                    }
                    catch (Exception exception)
                    {
                        failures.Add($"{snapshots[i].Path}: {exception.Message}");
                    }
                }

                return failures.Count == 0 ? null : string.Join(" | ", failures);
            }
        }

        private sealed class GeneratedAssetSnapshot
        {
            private readonly UnityEngine.Object asset;
            private readonly string serializedState;
            private readonly Color[] texturePixels;
            private readonly TextureWrapMode textureWrapMode;
            private readonly FilterMode textureFilterMode;
            private readonly int textureAnisoLevel;

            private GeneratedAssetSnapshot(
                string path,
                UnityEngine.Object asset,
                string serializedState,
                Color[] texturePixels,
                TextureWrapMode textureWrapMode,
                FilterMode textureFilterMode,
                int textureAnisoLevel)
            {
                Path = path;
                this.asset = asset;
                this.serializedState = serializedState;
                this.texturePixels = texturePixels;
                this.textureWrapMode = textureWrapMode;
                this.textureFilterMode = textureFilterMode;
                this.textureAnisoLevel = textureAnisoLevel;
            }

            public string Path { get; }

            public static GeneratedAssetSnapshot Capture(string path)
            {
                UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
                if (existing == null)
                {
                    return new GeneratedAssetSnapshot(
                        path,
                        null,
                        null,
                        null,
                        default,
                        default,
                        0);
                }

                if (EditorUtility.IsDirty(existing))
                {
                    throw new InvalidOperationException(
                        $"Generated asset is already dirty and will not be overwritten: '{path}'.");
                }

                if (existing is Texture2D texture)
                {
                    return new GeneratedAssetSnapshot(
                        path,
                        texture,
                        null,
                        texture.GetPixels(),
                        texture.wrapMode,
                        texture.filterMode,
                        texture.anisoLevel);
                }

                if (existing is DungeonPortalEndpointProfile profile)
                {
                    return new GeneratedAssetSnapshot(
                        path,
                        profile,
                        EditorJsonUtility.ToJson(profile, false),
                        null,
                        default,
                        default,
                        0);
                }

                throw new InvalidOperationException(
                    $"Unexpected generated asset type at '{path}': {existing.GetType().Name}.");
            }

            public void Restore()
            {
                if (asset == null)
                {
                    if (AssetDatabase.LoadMainAssetAtPath(Path) != null && !AssetDatabase.DeleteAsset(Path))
                        throw new InvalidOperationException("Could not delete a newly-created generated asset.");
                    return;
                }

                if (asset is Texture2D texture)
                {
                    texture.wrapMode = textureWrapMode;
                    texture.filterMode = textureFilterMode;
                    texture.anisoLevel = textureAnisoLevel;
                    texture.SetPixels(texturePixels);
                    texture.Apply(false, false);
                    EditorUtility.SetDirty(texture);
                    AssetDatabase.SaveAssetIfDirty(texture);
                    return;
                }

                if (asset is DungeonPortalEndpointProfile profile)
                {
                    EditorJsonUtility.FromJsonOverwrite(serializedState, profile);
                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssetIfDirty(profile);
                    return;
                }

                throw new InvalidOperationException("Captured generated asset changed type before rollback.");
            }
        }

        private readonly struct PrefabGuard
        {
            private PrefabGuard(string path, GameObject asset, Hash128 hash)
            {
                Path = path;
                Asset = asset;
                Hash = hash;
            }

            private string Path { get; }
            private GameObject Asset { get; }
            private Hash128 Hash { get; }

            public static bool TryCapture(
                string path,
                out PrefabGuard guard,
                out string failure)
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || !PrefabUtility.IsPartOfPrefabAsset(asset))
                {
                    guard = default;
                    failure = $"Production prefab is missing or invalid: '{path}'.";
                    return false;
                }

                if (EditorUtility.IsDirty(asset))
                {
                    guard = default;
                    failure = $"Production prefab is already dirty: '{path}'.";
                    return false;
                }

                guard = new PrefabGuard(path, asset, AssetDatabase.GetAssetDependencyHash(path));
                failure = null;
                return true;
            }

            public void AssertUnchanged()
            {
                Hash128 current = AssetDatabase.GetAssetDependencyHash(Path);
                if (current != Hash || EditorUtility.IsDirty(Asset))
                {
                    throw new InvalidOperationException(
                        $"Production prefab changed during endpoint authoring: '{Path}' " +
                        $"hashBefore={Hash} hashAfter={current}.");
                }
            }
        }
    }
}
