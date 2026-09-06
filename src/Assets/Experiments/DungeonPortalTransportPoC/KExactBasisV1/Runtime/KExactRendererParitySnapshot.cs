using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonPortalTransportPoC.KExactBasisV1
{
    internal sealed class KExactRendererParitySnapshot
    {
        private readonly Renderer renderer;
        private readonly EntityId entityId;
        private readonly bool enabled;
        private readonly bool forceRenderingOff;
        private readonly ShadowCastingMode shadowCastingMode;
        private readonly bool receiveShadows;
        private readonly int lightmapIndex;
        private readonly Vector4 lightmapScaleOffset;
        private readonly int realtimeLightmapIndex;
        private readonly Vector4 realtimeLightmapScaleOffset;
        private readonly LightProbeUsage lightProbeUsage;
        private readonly ReflectionProbeUsage reflectionProbeUsage;
        private readonly Transform probeAnchor;
        private readonly GameObject lightProbeProxyVolumeOverride;
        private readonly int sortingLayerId;
        private readonly int sortingOrder;
        private readonly Mesh mesh;
        private readonly MaterialSnapshot[] materials;
        private readonly bool propertyBlockWasEmpty;

        public Renderer Renderer => renderer;
        public uint OriginalRenderingLayerMask { get; }

        public KExactRendererParitySnapshot(Renderer target)
        {
            renderer = target != null
                ? target
                : throw new ArgumentNullException(nameof(target));
            entityId = target.GetEntityId();
            enabled = target.enabled;
            forceRenderingOff = target.forceRenderingOff;
            shadowCastingMode = target.shadowCastingMode;
            receiveShadows = target.receiveShadows;
            OriginalRenderingLayerMask = target.renderingLayerMask;
            lightmapIndex = target.lightmapIndex;
            lightmapScaleOffset = target.lightmapScaleOffset;
            realtimeLightmapIndex = target.realtimeLightmapIndex;
            realtimeLightmapScaleOffset = target.realtimeLightmapScaleOffset;
            lightProbeUsage = target.lightProbeUsage;
            reflectionProbeUsage = target.reflectionProbeUsage;
            probeAnchor = target.probeAnchor;
            lightProbeProxyVolumeOverride = target.lightProbeProxyVolumeOverride;
            sortingLayerId = target.sortingLayerID;
            sortingOrder = target.sortingOrder;
            mesh = ResolveSharedMesh(target);

            Material[] sharedMaterials = target.sharedMaterials ?? Array.Empty<Material>();
            materials = new MaterialSnapshot[sharedMaterials.Length];
            for (int i = 0; i < sharedMaterials.Length; i++)
                materials[i] = new MaterialSnapshot(sharedMaterials[i]);

            var block = new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            propertyBlockWasEmpty = block.isEmpty;
        }

        public bool MatchesExceptRenderingLayer(out string failure)
        {
            if (renderer == null || renderer.GetEntityId() != entityId)
            {
                failure = "Renderer was destroyed or replaced while its K-exact layer lease was active.";
                return false;
            }

            if (renderer.enabled != enabled ||
                renderer.forceRenderingOff != forceRenderingOff ||
                renderer.shadowCastingMode != shadowCastingMode ||
                renderer.receiveShadows != receiveShadows ||
                renderer.lightmapIndex != lightmapIndex ||
                renderer.lightmapScaleOffset != lightmapScaleOffset ||
                renderer.realtimeLightmapIndex != realtimeLightmapIndex ||
                renderer.realtimeLightmapScaleOffset != realtimeLightmapScaleOffset ||
                renderer.lightProbeUsage != lightProbeUsage ||
                renderer.reflectionProbeUsage != reflectionProbeUsage ||
                renderer.probeAnchor != probeAnchor ||
                renderer.lightProbeProxyVolumeOverride != lightProbeProxyVolumeOverride ||
                renderer.sortingLayerID != sortingLayerId ||
                renderer.sortingOrder != sortingOrder ||
                ResolveSharedMesh(renderer) != mesh)
            {
                failure = $"Renderer parity drift detected on '{GetPath(renderer.transform)}'.";
                return false;
            }

            Material[] currentMaterials = renderer.sharedMaterials ?? Array.Empty<Material>();
            if (currentMaterials.Length != materials.Length)
            {
                failure = $"Material-array length drift detected on '{GetPath(renderer.transform)}'.";
                return false;
            }

            for (int i = 0; i < materials.Length; i++)
            {
                if (!materials[i].Matches(currentMaterials[i]))
                {
                    failure = $"Material/shader/keyword/render-queue drift detected on " +
                              $"'{GetPath(renderer.transform)}' slot {i}.";
                    return false;
                }
            }

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            if (block.isEmpty != propertyBlockWasEmpty)
            {
                failure = $"MaterialPropertyBlock empty/non-empty parity drift detected on " +
                          $"'{GetPath(renderer.transform)}'.";
                return false;
            }

            failure = null;
            return true;
        }

        private static Mesh ResolveSharedMesh(Renderer target)
        {
            if (target is SkinnedMeshRenderer skinned)
                return skinned.sharedMesh;

            MeshFilter filter = target.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        private static string GetPath(Transform target)
        {
            if (target == null)
                return "<missing>";

            string path = target.name;
            Transform current = target.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private readonly struct MaterialSnapshot
        {
            private readonly Material material;
            private readonly Shader shader;
            private readonly int renderQueue;
            private readonly string[] keywords;

            public MaterialSnapshot(Material source)
            {
                material = source;
                shader = source != null ? source.shader : null;
                renderQueue = source != null ? source.renderQueue : 0;
                keywords = source != null
                    ? (string[])source.shaderKeywords.Clone()
                    : Array.Empty<string>();
                Array.Sort(keywords, StringComparer.Ordinal);
            }

            public bool Matches(Material current)
            {
                if (current != material)
                    return false;
                if (current == null)
                    return true;
                if (current.shader != shader || current.renderQueue != renderQueue)
                    return false;

                string[] currentKeywords = (string[])current.shaderKeywords.Clone();
                Array.Sort(currentKeywords, StringComparer.Ordinal);
                if (currentKeywords.Length != keywords.Length)
                    return false;

                for (int i = 0; i < keywords.Length; i++)
                {
                    if (!string.Equals(
                            currentKeywords[i],
                            keywords[i],
                            StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
