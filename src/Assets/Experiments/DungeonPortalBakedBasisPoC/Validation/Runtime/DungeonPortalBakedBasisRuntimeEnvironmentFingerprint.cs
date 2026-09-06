using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DungeonPortalBakedBasisPoC.Validation
{
    /// <summary>
    /// Read-only, per-Play-session environment contract for the StartMap dungeon-entry harness.
    /// It deliberately excludes the DPBB validation pair because room power, aperture and its
    /// owned reflection probes are the variables under test. Everything else is fail-closed.
    /// </summary>
    public sealed class DungeonPortalBakedBasisRuntimeEnvironmentFingerprint
    {
        private const float PositionTolerance = 0.001f;
        private const float RotationToleranceDegrees = 0.01f;

        private readonly string canonical;
        private readonly CameraFrame[] fixedFrames;

        public string Sha256 { get; }
        public string Summary => canonical;
        public int FixedFrameCount => fixedFrames.Length;

        private DungeonPortalBakedBasisRuntimeEnvironmentFingerprint(
            string capturedCanonical,
            CameraFrame[] capturedFrames)
        {
            canonical = capturedCanonical;
            fixedFrames = capturedFrames ?? Array.Empty<CameraFrame>();
            Sha256 = ComputeSha256(canonical);
        }

        public static bool TryCapture(
            Camera actualPlayerCamera,
            Volume assignedDungeonVolume,
            GameObject validationPairRoot,
            Camera[] fixedFrameReferences,
            DungeonPortalBakedBasisEvidenceSpotPolicy spotPolicy,
            out DungeonPortalBakedBasisRuntimeEnvironmentFingerprint fingerprint,
            out string failure)
        {
            fingerprint = null;
            if (!TryBuildCanonical(
                    actualPlayerCamera,
                    assignedDungeonVolume,
                    validationPairRoot,
                    fixedFrameReferences,
                    spotPolicy,
                    out string value,
                    out CameraFrame[] frames,
                    out failure))
            {
                return false;
            }

            fingerprint = new DungeonPortalBakedBasisRuntimeEnvironmentFingerprint(value, frames);
            failure = null;
            return true;
        }

        /// <summary>
        /// Validates the full StartMap environment before one capture. The real player camera is
        /// allowed to move only to the selected disabled reference-camera frame.
        /// </summary>
        public bool TryValidateCurrent(
            Camera actualPlayerCamera,
            Volume assignedDungeonVolume,
            GameObject validationPairRoot,
            Camera[] fixedFrameReferences,
            DungeonPortalBakedBasisEvidenceSpotPolicy spotPolicy,
            int expectedFrameIndex,
            out string failure)
        {
            if (!TryBuildCanonical(
                    actualPlayerCamera,
                    assignedDungeonVolume,
                    validationPairRoot,
                    fixedFrameReferences,
                    spotPolicy,
                    out string current,
                    out CameraFrame[] currentFrames,
                    out failure))
            {
                return false;
            }

            if (!string.Equals(Sha256, ComputeSha256(current), StringComparison.Ordinal))
            {
                failure = "StartMap runtime environment fingerprint drifted. expected=" + Sha256 +
                          " actual=" + ComputeSha256(current);
                return false;
            }

            if (expectedFrameIndex < 0 || expectedFrameIndex >= fixedFrames.Length ||
                expectedFrameIndex >= currentFrames.Length)
            {
                failure = "Requested fixed camera frame is outside the captured environment contract.";
                return false;
            }

            CameraFrame expected = fixedFrames[expectedFrameIndex];
            CameraFrame currentReference = currentFrames[expectedFrameIndex];
            if (!expected.Equals(currentReference))
            {
                failure = "Disabled reference-camera frame drifted after READY.";
                return false;
            }

            if (actualPlayerCamera == null ||
                Vector3.Distance(actualPlayerCamera.transform.position, expected.Position) >
                PositionTolerance ||
                Quaternion.Angle(actualPlayerCamera.transform.rotation, expected.Rotation) >
                RotationToleranceDegrees)
            {
                failure = "Actual player camera is not at the requested fixed capture frame.";
                return false;
            }

            failure = null;
            return true;
        }

        private static bool TryBuildCanonical(
            Camera actualPlayerCamera,
            Volume assignedDungeonVolume,
            GameObject validationPairRoot,
            Camera[] fixedFrameReferences,
            DungeonPortalBakedBasisEvidenceSpotPolicy spotPolicy,
            out string result,
            out CameraFrame[] frames,
            out string failure)
        {
            result = null;
            frames = null;
            if (actualPlayerCamera == null || !actualPlayerCamera.enabled ||
                !actualPlayerCamera.gameObject.activeInHierarchy || actualPlayerCamera.targetTexture != null)
            {
                failure = "A live actual-player Camera without a target texture is required.";
                return false;
            }
            if (assignedDungeonVolume == null || !assignedDungeonVolume.enabled ||
                !assignedDungeonVolume.gameObject.activeInHierarchy ||
                assignedDungeonVolume.sharedProfile == null || validationPairRoot == null)
            {
                failure = "Assigned active dungeon Volume and validation pair are required.";
                return false;
            }
            if (!TryCaptureFrames(fixedFrameReferences, out frames, out failure))
                return false;

            var builder = new StringBuilder(8192);
            builder.AppendLine("schema=DPBB_StartMapEnvironment/v2");
            builder.AppendLine("spotPolicy=" + spotPolicy);
            AppendLoadedScenes(builder);
            AppendCamera(builder, actualPlayerCamera);
            AppendVolume(builder, assignedDungeonVolume);
            AppendAllAffectingVolumes(builder);
            AppendRenderSettings(builder);
            if (!TryAppendProductionLightmaps(builder, out failure))
                return false;
            AppendQualityAndPipeline(builder);
            AppendExteriorAndAltos(builder, validationPairRoot.transform);
            AppendExternalReflection(builder, validationPairRoot.transform);
            AppendFrames(builder, frames);
            result = builder.ToString();
            failure = null;
            return true;
        }

        private static bool TryCaptureFrames(
            Camera[] source,
            out CameraFrame[] frames,
            out string failure)
        {
            source = source ?? Array.Empty<Camera>();
            if (source.Length != 2)
            {
                frames = null;
                failure = "Exactly two disabled DPBB reference cameras are required; no fallback camera is allowed.";
                return false;
            }

            frames = new CameraFrame[source.Length];
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.Length; i++)
            {
                Camera camera = source[i];
                if (camera == null || camera.enabled || camera.targetTexture != null ||
                    !camera.gameObject.activeInHierarchy || !names.Add(camera.name))
                {
                    failure = "Reference camera " + i +
                              " is missing, enabled, rendered to a texture, inactive, or duplicated.";
                    return false;
                }
                frames[i] = new CameraFrame(camera.name, camera.transform.position,
                    camera.transform.rotation);
            }

            failure = null;
            return true;
        }

        private static void AppendLoadedScenes(StringBuilder builder)
        {
            builder.Append("sceneCount=").Append(SceneManager.sceneCount).AppendLine();
            builder.Append("activeScene=").Append(SceneManager.GetActiveScene().path).AppendLine();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                builder.Append("scene=").Append(i).Append('|').Append(scene.path).Append('|')
                    .Append(scene.name).Append('|').Append(scene.isLoaded).AppendLine();
            }
        }

        private static void AppendCamera(StringBuilder builder, Camera camera)
        {
            UniversalAdditionalCameraData urp = camera.GetUniversalAdditionalCameraData();
            builder.Append("camera=").Append(ObjectId(camera)).Append('|')
                .Append("parent=").Append(HierarchyPath(camera.transform.parent)).Append('|')
                .Append("localPosition=").Append(Vector(camera.transform.localPosition)).Append('|')
                .Append("localRotation=").Append(QuaternionValue(camera.transform.localRotation)).Append('|')
                .Append("clear=").Append(camera.clearFlags).Append('|')
                .Append("culling=").Append(camera.cullingMask).Append('|')
                .Append("depth=").Append(Float(camera.depth)).Append('|')
                .Append("fov=").Append(Float(camera.fieldOfView)).Append('|')
                .Append("near=").Append(Float(camera.nearClipPlane)).Append('|')
                .Append("far=").Append(Float(camera.farClipPlane)).Append('|')
                .Append("hdr=").Append(camera.allowHDR).Append('|')
                .Append("msaa=").Append(camera.allowMSAA).Append('|')
                .Append("orthographic=").Append(camera.orthographic).Append('|')
                .Append("main=").Append(Camera.main == camera).Append('|')
                .Append("urp=").Append(ObjectId(urp)).Append('|')
                .Append("urpRenderType=").Append(urp != null ? urp.renderType.ToString() : "MISSING").Append('|')
                .Append("urpPost=").Append(urp != null && urp.renderPostProcessing).AppendLine();
        }

        private static void AppendVolume(StringBuilder builder, Volume volume)
        {
            VolumeProfile profile = volume.sharedProfile;
            builder.Append("volume=").Append(ObjectId(volume)).Append('|')
                .Append("global=").Append(volume.isGlobal).Append('|')
                .Append("priority=").Append(Float(volume.priority)).Append('|')
                .Append("weight=").Append(Float(volume.weight)).Append('|')
                .Append("blendDistance=").Append(Float(volume.blendDistance)).Append('|')
                .Append("profile=").Append(ObjectId(profile)).AppendLine();
            if (profile == null)
                return;
            List<VolumeComponent> components = new List<VolumeComponent>(profile.components);
            components.Sort((left, right) => string.CompareOrdinal(
                left != null ? left.GetType().FullName : string.Empty,
                right != null ? right.GetType().FullName : string.Empty));
            for (int i = 0; i < components.Count; i++)
            {
                VolumeComponent component = components[i];
                builder.Append("volumeComponent=").Append(component != null ? component.GetType().FullName : "NULL")
                    .Append('|').Append(component != null && component.active).AppendLine();
            }
        }

        private static void AppendRenderSettings(StringBuilder builder)
        {
            builder.Append("render.skybox=").Append(ObjectId(RenderSettings.skybox)).Append('|')
                .Append("ambientMode=").Append(RenderSettings.ambientMode).Append('|')
                .Append("ambientLight=").Append(ColorValue(RenderSettings.ambientLight)).Append('|')
                .Append("ambientIntensity=").Append(Float(RenderSettings.ambientIntensity)).Append('|')
                .Append("reflectionIntensity=").Append(Float(RenderSettings.reflectionIntensity)).Append('|')
                .Append("reflectionBounces=").Append(RenderSettings.reflectionBounces).Append('|')
                .Append("defaultReflectionMode=").Append(RenderSettings.defaultReflectionMode).Append('|')
                .Append("defaultReflectionResolution=").Append(RenderSettings.defaultReflectionResolution).Append('|')
                .Append("customReflection=").Append(ObjectId(RenderSettings.customReflectionTexture)).Append('|')
                .Append("fog=").Append(RenderSettings.fog).Append('|')
                .Append("fogMode=").Append(RenderSettings.fogMode).Append('|')
                .Append("fogColor=").Append(ColorValue(RenderSettings.fogColor)).Append('|')
                .Append("fogDensity=").Append(Float(RenderSettings.fogDensity)).AppendLine();
            SphericalHarmonicsL2 sh = RenderSettings.ambientProbe;
            builder.Append("ambientProbe=");
            for (int channel = 0; channel < 3; channel++)
            for (int coefficient = 0; coefficient < 9; coefficient++)
                builder.Append(Float(sh[channel, coefficient])).Append(',');
            builder.AppendLine();
        }

        private static void AppendAllAffectingVolumes(StringBuilder builder)
        {
            Volume[] volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include);
            var entries = new List<string>(volumes.Length);
            for (int i = 0; i < volumes.Length; i++)
            {
                Volume volume = volumes[i];
                if (volume == null) continue;
                VolumeProfile profile = volume.sharedProfile;
                entries.Add(HierarchyPath(volume.transform) + "|" + ObjectId(volume) + "|" +
                            volume.enabled + "|" + volume.gameObject.activeInHierarchy + "|" +
                            volume.isGlobal + "|" + Float(volume.priority) + "|" +
                            Float(volume.weight) + "|" + Float(volume.blendDistance) + "|" +
                            ObjectId(profile));
            }
            entries.Sort(StringComparer.Ordinal);
            builder.Append("allVolumeCount=").Append(entries.Count).AppendLine();
            for (int i = 0; i < entries.Count; i++)
                builder.Append("volumeState=").Append(entries[i]).AppendLine();
        }

        private static bool TryAppendProductionLightmaps(
            StringBuilder builder,
            out string failure)
        {
            if (!DungeonPortalBakedBasisLightmapRegistry.TryGetProductionLayoutForFingerprint(
                    out LightmapsMode mode,
                    out LightmapData[] maps,
                    out failure))
            {
                failure = "DPBB registry could not prove its production lightmap prefix: " + failure;
                return false;
            }
            builder.Append("lightmapsMode=").Append(mode).Append('|')
                .Append("lightmapCount=").Append(maps.Length).Append('|')
                .Append("dpbbPrivateSlots=normalized|")
                .Append("lightProbes=").Append(ObjectId(LightmapSettings.lightProbes)).AppendLine();
            for (int i = 0; i < maps.Length; i++)
            {
                LightmapData map = maps[i];
                builder.Append("lightmap=").Append(i).Append('|')
                    .Append(ObjectId(map != null ? map.lightmapColor : null)).Append('|')
                    .Append(ObjectId(map != null ? map.lightmapDir : null)).Append('|')
                    .Append(ObjectId(map != null ? map.shadowMask : null)).AppendLine();
            }
            failure = null;
            return true;
        }

        private static void AppendQualityAndPipeline(StringBuilder builder)
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            builder.Append("quality.level=").Append(QualitySettings.GetQualityLevel()).Append('|')
                .Append("quality.name=").Append(QualitySettings.names[QualitySettings.GetQualityLevel()]).Append('|')
                .Append("quality.colorSpace=").Append(QualitySettings.activeColorSpace).Append('|')
                .Append("quality.pixelLights=").Append(QualitySettings.pixelLightCount).Append('|')
                .Append("quality.shadows=").Append(QualitySettings.shadows).Append('|')
                .Append("quality.shadowDistance=").Append(Float(QualitySettings.shadowDistance)).Append('|')
                .Append("pipeline=").Append(ObjectId(pipeline)).Append('|')
                .Append("pipelineType=").Append(pipeline != null ? pipeline.GetType().FullName : "MISSING")
                .AppendLine();
        }

        private static void AppendExteriorAndAltos(StringBuilder builder, Transform pairRoot)
        {
            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
            var directional = new List<string>();
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null || light.transform == pairRoot || light.transform.IsChildOf(pairRoot) ||
                    light.type != LightType.Directional)
                {
                    continue;
                }
                directional.Add(HierarchyPath(light.transform) + "|" + ObjectId(light) + "|" +
                                light.enabled + "|" + light.gameObject.activeInHierarchy + "|" +
                                Float(light.intensity) + "|" + light.renderingLayerMask + "|" +
                                light.shadows + "|" + QuaternionValue(light.transform.rotation));
            }
            directional.Sort(StringComparer.Ordinal);
            builder.Append("exteriorDirectionalCount=").Append(directional.Count).AppendLine();
            for (int i = 0; i < directional.Count; i++)
                builder.Append("exteriorDirectional=").Append(directional[i]).AppendLine();

            Behaviour[] behaviours = UnityEngine.Object.FindObjectsByType<Behaviour>(FindObjectsInactive.Include);
            var altos = new List<string>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                string typeName = behaviour != null ? behaviour.GetType().FullName : string.Empty;
                if (string.IsNullOrEmpty(typeName) || behaviour.transform == pairRoot ||
                    behaviour.transform.IsChildOf(pairRoot) ||
                    (typeName.IndexOf("Altos", StringComparison.OrdinalIgnoreCase) < 0 &&
                     typeName.IndexOf("Cloud", StringComparison.OrdinalIgnoreCase) < 0 &&
                     typeName.IndexOf("TerrainAmbience", StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }
                altos.Add(HierarchyPath(behaviour.transform) + "|" + typeName + "|" +
                          behaviour.enabled + "|" + behaviour.gameObject.activeInHierarchy);
            }
            altos.Sort(StringComparer.Ordinal);
            builder.Append("altosExteriorBehaviourCount=").Append(altos.Count).AppendLine();
            for (int i = 0; i < altos.Count; i++)
                builder.Append("altosExterior=").Append(altos[i]).AppendLine();
        }

        private static void AppendExternalReflection(StringBuilder builder, Transform pairRoot)
        {
            ReflectionProbe[] probes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include);
            var entries = new List<string>(probes.Length);
            for (int i = 0; i < probes.Length; i++)
            {
                ReflectionProbe probe = probes[i];
                if (probe == null || probe.transform == pairRoot || probe.transform.IsChildOf(pairRoot))
                    continue;
                entries.Add(HierarchyPath(probe.transform) + "|" + ObjectId(probe) + "|" +
                            probe.enabled + "|" + probe.gameObject.activeInHierarchy + "|" +
                            probe.mode + "|" + probe.refreshMode + "|" + probe.timeSlicingMode + "|" +
                            ObjectId(probe.customBakedTexture) + "|" + Float(probe.intensity) + "|" +
                            Vector(probe.size) + "|" + Vector(probe.center) + "|" +
                            Float(probe.blendDistance) + "|" + probe.boxProjection + "|" +
                            probe.cullingMask + "|" + Vector(probe.transform.position) + "|" +
                            QuaternionValue(probe.transform.rotation));
            }
            entries.Sort(StringComparer.Ordinal);
            builder.Append("externalReflectionCount=").Append(entries.Count).AppendLine();
            for (int i = 0; i < entries.Count; i++)
                builder.Append("reflection=").Append(entries[i]).AppendLine();
        }

        private static void AppendFrames(StringBuilder builder, CameraFrame[] frames)
        {
            for (int i = 0; i < frames.Length; i++)
                builder.Append("frame=").Append(i).Append('|').Append(frames[i].Name).Append('|')
                    .Append(Vector(frames[i].Position)).Append('|')
                    .Append(QuaternionValue(frames[i].Rotation)).AppendLine();
        }

        private static string ComputeSha256(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static string ObjectId(UnityEngine.Object value)
        {
            return value == null ? "NULL" : value.GetType().FullName + ":" + value.name;
        }

        private static string HierarchyPath(Transform transform)
        {
            if (transform == null)
                return "NULL";
            var parts = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                parts.Push(current.name);
                current = current.parent;
            }
            return transform.gameObject.scene.path + ":" + string.Join("/", parts);
        }

        private static string Float(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Vector(Vector3 value)
        {
            return Float(value.x) + "," + Float(value.y) + "," + Float(value.z);
        }

        private static string QuaternionValue(Quaternion value)
        {
            return Float(value.x) + "," + Float(value.y) + "," + Float(value.z) + "," + Float(value.w);
        }

        private static string ColorValue(Color value)
        {
            return Float(value.r) + "," + Float(value.g) + "," + Float(value.b) + "," + Float(value.a);
        }

        private readonly struct CameraFrame : IEquatable<CameraFrame>
        {
            internal readonly string Name;
            internal readonly Vector3 Position;
            internal readonly Quaternion Rotation;

            internal CameraFrame(string name, Vector3 position, Quaternion rotation)
            {
                Name = name;
                Position = position;
                Rotation = rotation;
            }

            public bool Equals(CameraFrame other)
            {
                return string.Equals(Name, other.Name, StringComparison.Ordinal) &&
                       Vector3.Distance(Position, other.Position) <= PositionTolerance &&
                       Quaternion.Angle(Rotation, other.Rotation) <= RotationToleranceDegrees;
            }

            public override bool Equals(object obj)
            {
                return obj is CameraFrame other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (Name ?? string.Empty).GetHashCode();
            }
        }
    }
}
