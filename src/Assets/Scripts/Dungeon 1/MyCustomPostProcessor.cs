using DunGen;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class MyCustomPostProcessor : MonoBehaviour
{
    public RuntimeDungeon RuntimeDungeon;
    [SerializeField] private Transform dungeonRoot;

    [Header("P0-1 Exterior Light Isolation")]
    [SerializeField] private bool enforceDungeonRenderingLayer = true;
    [SerializeField] private string dungeonRenderingLayerName = "Dungeon";

    [Header("Diagnostics")]
    [SerializeField] private bool enableStepTimingDiagnostics = true;
    [SerializeField] private bool enablePerTileTimingDiagnostics;

    private bool _postProcessRegistered;

    private void Awake()
    {
        TryRegisterPostProcess();
    }

    private void Start()
    {
        TryRegisterPostProcess();
    }

    private void OnEnable()
    {
        TryRegisterPostProcess();
    }

    private void OnDisable()
    {
        UnregisterPostProcess();
    }

    private void OnDestroy()
    {
        UnregisterPostProcess();
    }

    private void TryRegisterPostProcess()
    {
        if (_postProcessRegistered)
            return;

        if (RuntimeDungeon == null)
            RuntimeDungeon = GetComponent<RuntimeDungeon>();

        if (RuntimeDungeon == null)
            return;

        RuntimeDungeon.Generator.RegisterPostProcessStep(MyCustomPostProcessLogic, 1, PostProcessPhase.AfterBuiltIn);
        _postProcessRegistered = true;
    }

    private void UnregisterPostProcess()
    {
        if (!_postProcessRegistered || RuntimeDungeon == null)
            return;

        RuntimeDungeon.Generator.UnregisterPostProcessStep(MyCustomPostProcessLogic);
        _postProcessRegistered = false;
    }

    private void MyCustomPostProcessLogic(DunGen.DungeonGenerator generator)
    {
        if (generator == null || generator.CurrentDungeon == null || generator.CurrentDungeon.AllTiles == null)
        {
            Debug.LogWarning("[MyCustomPostProcessor] Dungeon data is missing.", this);
            return;
        }

        Dungeon dungeon = generator.CurrentDungeon;

        float totalStart = Time.realtimeSinceStartup;

        if (enableStepTimingDiagnostics)
            Debug.Log($"[DungeonGenDiag][MyCustomPost] begin tiles={dungeon.AllTiles.Count}", this);

        float policyStart = Time.realtimeSinceStartup;
        ApplyTilePolicies(dungeon, out int totalLights, out int rendererChanged, out int lightLayerChanged);
        float policyMs = (Time.realtimeSinceStartup - policyStart) * 1000f;

        if (enableStepTimingDiagnostics)
            Debug.Log($"[DungeonGenDiag][MyCustomPost] policy-complete ms={policyMs:0.0}", this);

        float registryStart = Time.realtimeSinceStartup;
        if (enableStepTimingDiagnostics)
            Debug.Log("[DungeonGenDiag][MyCustomPost] registry-begin", this);
        if (dungeonRoot != null)
            DungeonPointRegistry.RebuildFromRoot(dungeonRoot);
        float registryMs = (Time.realtimeSinceStartup - registryStart) * 1000f;

        if (enableStepTimingDiagnostics)
            Debug.Log($"[DungeonGenDiag][MyCustomPost] registry-complete ms={registryMs:0.0}", this);

        if (enableStepTimingDiagnostics)
        {
            float totalMs = (Time.realtimeSinceStartup - totalStart) * 1000f;
            Debug.Log(
                $"[DungeonGenDiag][MyCustomPost] totalMs={totalMs:0.0} policyMs={policyMs:0.0} " +
                $"registryMs={registryMs:0.0} totalLights={totalLights} rendererChanged={rendererChanged} " +
                $"lightLayerChanged={lightLayerChanged} tiles={dungeon.AllTiles.Count}",
                this);
        }
    }

    private void ApplyTilePolicies(Dungeon dungeon, out int totalLights, out int rendererChanged, out int lightLayerChanged)
    {
        totalLights = 0;
        rendererChanged = 0;
        lightLayerChanged = 0;

        int layerIndex = -1;
        uint targetRendererMask = 0;
        int targetLightMask = 0;
        bool applyLayer = enforceDungeonRenderingLayer;

        if (applyLayer)
        {
            layerIndex = RenderingLayerMask.NameToRenderingLayer(dungeonRenderingLayerName);
            if (layerIndex < 0)
            {
                Debug.LogError($"[MyCustomPostProcessor] Rendering Layer '{dungeonRenderingLayerName}' not found.", this);
                applyLayer = false;
            }
            else
            {
                targetRendererMask = 1u << layerIndex;
                targetLightMask = 1 << layerIndex;
            }
        }

        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            Tile tile = dungeon.AllTiles[i];
            if (tile == null)
                continue;

            float tileStart = Time.realtimeSinceStartup;
            if (enablePerTileTimingDiagnostics)
                Debug.Log($"[DungeonGenDiag][MyCustomPost] tile-begin index={i} name={tile.name}", tile);

            Renderer[] renderers = tile.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer targetRenderer = renderers[r];
                if (targetRenderer == null || !applyLayer)
                    continue;

                if (targetRenderer.renderingLayerMask != targetRendererMask)
                {
                    targetRenderer.renderingLayerMask = targetRendererMask;
                    rendererChanged++;
                }
            }

            Light[] lights = tile.GetComponentsInChildren<Light>(true);
            for (int j = 0; j < lights.Length; j++)
            {
                Light lightComponent = lights[j];
                if (lightComponent == null)
                    continue;

                totalLights++;

                if (!applyLayer)
                    continue;

                if (ApplyLightRenderingLayer(lightComponent, targetLightMask))
                    lightLayerChanged++;
            }

            if (enablePerTileTimingDiagnostics)
            {
                float tileMs = (Time.realtimeSinceStartup - tileStart) * 1000f;
                Debug.Log(
                    $"[DungeonGenDiag][MyCustomPost] tile-complete index={i} name={tile.name} " +
                    $"renderers={renderers.Length} lights={lights.Length} ms={tileMs:0.0}",
                    tile);
            }
        }
    }

    private static bool ApplyLightRenderingLayer(Light lightComponent, int targetLightMask)
    {
        bool changed = false;

        if (lightComponent.renderingLayerMask != targetLightMask)
        {
            lightComponent.renderingLayerMask = targetLightMask;
            changed = true;
        }

        var additionalLightData = lightComponent.GetComponent<UniversalAdditionalLightData>();
        if (additionalLightData != null)
        {
            uint targetRenderingLayers = unchecked((uint)targetLightMask);
            if (additionalLightData.renderingLayers != targetRenderingLayers)
            {
                additionalLightData.renderingLayers = targetRenderingLayers;
                changed = true;
            }

            if (additionalLightData.shadowRenderingLayers != targetRenderingLayers)
            {
                additionalLightData.shadowRenderingLayers = targetRenderingLayers;
                changed = true;
            }
        }

        return changed;
    }
}
