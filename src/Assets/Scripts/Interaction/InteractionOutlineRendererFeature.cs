using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>Draws an outer silhouette around the local player's hovered interaction.</summary>
public sealed class InteractionOutlineRendererFeature : ScriptableRendererFeature
{
    [SerializeField] private Shader outlineShader;
    [SerializeField] private Color outlineColor = new(1f, 212f / 255f, 0f, 0.75f);
    [SerializeField, Range(1, 6)] private int thickness = 2;

    private Material _material;
    private OutlinePass _pass;

    public override void Create()
    {
        CoreUtils.Destroy(_material);
        _material = outlineShader != null ? CoreUtils.CreateEngineMaterial(outlineShader) : null;
        _pass = new OutlinePass
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
        };
        _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_material == null || renderingData.cameraData.camera != InteractionHoverOutline.TargetCamera
            || InteractionHoverOutline.Renderers.Count == 0)
            return;

        _pass.Setup(_material, outlineColor, Mathf.Clamp(thickness, 1, 6));
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
    }

    private sealed class OutlinePass : ScriptableRenderPass
    {
        private static readonly int SceneDepthId = Shader.PropertyToID("_InteractionSceneDepth");
        private static readonly int MaskId = Shader.PropertyToID("_InteractionMask");
        private static readonly int ExpandedMaskId = Shader.PropertyToID("_InteractionExpandedMask");
        private static readonly int ColorId = Shader.PropertyToID("_InteractionOutlineColor");
        private static readonly int ThicknessId = Shader.PropertyToID("_InteractionOutlineThickness");
        private static readonly MaterialPropertyBlock Properties = new();

        private Material _material;
        private Color _color;
        private int _thickness;

        private sealed class MaskData
        {
            public Material material;
            public TextureHandle sceneDepth;
            public IReadOnlyList<Renderer> renderers;
            public int cullingMask;
        }

        private sealed class ExpandData
        {
            public Material material;
            public TextureHandle mask;
            public int thickness;
        }

        private sealed class CompositeData
        {
            public Material material;
            public TextureHandle mask;
            public TextureHandle expandedMask;
            public TextureHandle sceneDepth;
            public Color color;
            public int thickness;
        }

        public void Setup(Material material, Color color, int thickness)
        {
            _material = material;
            _color = color;
            _thickness = thickness;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var camera = frameData.Get<UniversalCameraData>().camera;
            if (camera != InteractionHoverOutline.TargetCamera || InteractionHoverOutline.Renderers.Count == 0
                || !resources.cameraDepthTexture.IsValid())
                return;

            // Store reciprocal eye depth: zero means empty and Max blending selects the nearest surface.
            // A resolved mask can sample the requested camera depth independently of the camera's MSAA.
            var descriptor = resources.activeColorTexture.GetDescriptor(renderGraph);
            descriptor.name = "Interaction outline mask";
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.colorFormat = GraphicsFormat.R32_SFloat;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.bindTextureMS = false;
            descriptor.filterMode = FilterMode.Point;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.clearBuffer = true;
            descriptor.clearColor = Color.clear;
            var mask = renderGraph.CreateTexture(descriptor);

            descriptor.name = "Interaction outline horizontal expansion";
            descriptor.clearBuffer = false;
            var expandedMask = renderGraph.CreateTexture(descriptor);

            using (var builder = renderGraph.AddRasterRenderPass<MaskData>("Interaction outline mask", out var data))
            {
                data.material = _material;
                data.sceneDepth = resources.cameraDepthTexture;
                data.renderers = InteractionHoverOutline.Renderers;
                data.cullingMask = camera.cullingMask;
                builder.UseTexture(data.sceneDepth, AccessFlags.Read);
                builder.SetRenderAttachment(mask, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (MaskData pass, RasterGraphContext context) =>
                {
                    pass.material.SetTexture(SceneDepthId, pass.sceneDepth);
                    for (int i = 0; i < pass.renderers.Count; i++)
                    {
                        var target = pass.renderers[i];
                        if (target == null || !target.enabled || target.forceRenderingOff
                            || !target.gameObject.activeInHierarchy
                            || (pass.cullingMask & (1 << target.gameObject.layer)) == 0)
                            continue;

                        Mesh mesh = null;
                        if (target is SkinnedMeshRenderer skinned)
                            mesh = skinned.sharedMesh;
                        else if (target.TryGetComponent<MeshFilter>(out var filter))
                            mesh = filter.sharedMesh;

                        if (mesh == null)
                            continue;

                        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                            context.cmd.DrawRenderer(target, pass.material, submesh, 0);
                    }
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<ExpandData>("Interaction outline expand", out var data))
            {
                data.material = _material;
                data.mask = mask;
                data.thickness = _thickness;
                builder.UseTexture(data.mask, AccessFlags.Read);
                builder.SetRenderAttachment(expandedMask, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc(static (ExpandData pass, RasterGraphContext context) =>
                {
                    Properties.Clear();
                    Properties.SetTexture(MaskId, pass.mask);
                    Properties.SetInt(ThicknessId, pass.thickness);
                    context.cmd.DrawProcedural(Matrix4x4.identity, pass.material, 1,
                        MeshTopology.Triangles, 3, 1, Properties);
                });
            }

            // This is a custom raster pass because it samples three textures and alpha-blends into
            // the existing color attachment; the camera color is never copied or sampled.
            using (var builder = renderGraph.AddRasterRenderPass<CompositeData>("Interaction outline composite", out var data))
            {
                data.material = _material;
                data.mask = mask;
                data.expandedMask = expandedMask;
                data.sceneDepth = resources.cameraDepthTexture;
                data.color = _color;
                data.thickness = _thickness;
                builder.UseTexture(data.mask, AccessFlags.Read);
                builder.UseTexture(data.expandedMask, AccessFlags.Read);
                builder.UseTexture(data.sceneDepth, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (CompositeData pass, RasterGraphContext context) =>
                {
                    Properties.Clear();
                    Properties.SetTexture(MaskId, pass.mask);
                    Properties.SetTexture(ExpandedMaskId, pass.expandedMask);
                    Properties.SetTexture(SceneDepthId, pass.sceneDepth);
                    Properties.SetColor(ColorId, pass.color);
                    Properties.SetInt(ThicknessId, pass.thickness);
                    context.cmd.DrawProcedural(Matrix4x4.identity, pass.material, 2,
                        MeshTopology.Triangles, 3, 1, Properties);
                });
            }
        }
    }
}
