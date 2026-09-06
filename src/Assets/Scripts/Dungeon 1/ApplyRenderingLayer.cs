using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

using UnityEngine.Rendering.Universal;

public class ApplyRenderingLayer : MonoBehaviour
{
    [Header("Rendering Layer Names (Project Settings > Tags and Layers > Rendering Layers)")]
    [SerializeField] private string dungeonName = "Dungeon";
    [SerializeField] private string defaultName = "Default";

    [Header("Options")]
    [SerializeField] private bool includeInactive = true;

    uint Mask(string name)
    {
        int idx = RenderingLayerMask.NameToRenderingLayer(name);
        if (idx < 0)
        {
            Debug.LogError($"Rendering Layer '{name}' not found. (Tags and Layers > Rendering Layers)");
            return 0;
        }
        return 1u << idx;
    }

#if UNITY_EDITOR
    void MarkPrefabOrSceneDirtyAndSave()
    {
        var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null) EditorSceneManager.MarkSceneDirty(stage.scene);
        else EditorSceneManager.MarkSceneDirty(gameObject.scene);

        AssetDatabase.SaveAssets();
    }
#endif

    bool SetRendererMask(Renderer r, uint targetMask)
    {
        if (!r) return false;
        if (r.renderingLayerMask == targetMask) return false;

#if UNITY_EDITOR
        Undo.RecordObject(r, "Set Renderer Rendering Layer Mask");
#endif
        r.renderingLayerMask = targetMask;
#if UNITY_EDITOR
        EditorUtility.SetDirty(r);
#endif
        return true;
    }

    bool SetLightMask(Light l, uint targetMask)
    {
        if (!l) return false;

        bool changed = false;

        // URP는 Additional Light Data에 실제 마스크가 저장되는 경우가 많음
        var add = l.GetComponent<UniversalAdditionalLightData>();
        if (add != null)
        {
            if (add.renderingLayers != targetMask)
            {
#if UNITY_EDITOR
                Undo.RecordObject(add, "Set URP Light Rendering Layers");
#endif
                add.renderingLayers = targetMask;
                changed = true;
#if UNITY_EDITOR
                EditorUtility.SetDirty(add);
#endif
            }

            if (add.shadowRenderingLayers != targetMask)
            {
#if UNITY_EDITOR
                Undo.RecordObject(add, "Set URP Light Shadow Rendering Layers");
#endif
                add.shadowRenderingLayers = targetMask;
                changed = true;
#if UNITY_EDITOR
                EditorUtility.SetDirty(add);
#endif
            }
        }

        // Unity Light에도 있으면 같이 맞춤 (버전에 따라 int)
        try
        {
            int m = unchecked((int)targetMask);
            if (l.renderingLayerMask != m)
            {
#if UNITY_EDITOR
                Undo.RecordObject(l, "Set Light Rendering Layer Mask");
#endif
                l.renderingLayerMask = m;
                changed = true;
#if UNITY_EDITOR
                EditorUtility.SetDirty(l);
#endif
            }
        }
        catch { }

        return changed;
    }

    [ContextMenu("Apply Dungeon: Renderers=Dungeon, Lights=Dungeon")]
    public void ApplyDungeon_All()
    {
        uint dungeon = Mask(dungeonName);

        int rTotal = 0, rChanged = 0;
        foreach (var r in GetComponentsInChildren<Renderer>(includeInactive))
        {
            rTotal++;
            if (SetRendererMask(r, dungeon)) rChanged++;
        }

        int lTotal = 0, lChanged = 0, missing = 0;
        foreach (var l in GetComponentsInChildren<Light>(includeInactive))
        {
            lTotal++;
            if (l.GetComponent<UniversalAdditionalLightData>() == null) missing++;
            if (SetLightMask(l, dungeon)) lChanged++;
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
        MarkPrefabOrSceneDirtyAndSave();
#endif

        Debug.Log(
            $"[ApplyDungeonRenderingLayer] '{name}'\n" +
            $"- Renderers changed {rChanged}/{rTotal} -> {dungeonName}\n" +
            $"- Lights    changed {lChanged}/{lTotal} -> {dungeonName}\n" +
            $"- Lights missing UniversalAdditionalLightData: {missing}"
        );
    }

    [ContextMenu("Restore All To Default (Renderers+Lights)")]
    public void RestoreAllToDefault()
    {
        uint def = Mask(defaultName);

        int rTotal = 0, rChanged = 0;
        foreach (var r in GetComponentsInChildren<Renderer>(includeInactive))
        {
            rTotal++;
            if (SetRendererMask(r, def)) rChanged++;
        }

        int lTotal = 0, lChanged = 0;
        foreach (var l in GetComponentsInChildren<Light>(includeInactive))
        {
            lTotal++;
            if (SetLightMask(l, def)) lChanged++;
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
        MarkPrefabOrSceneDirtyAndSave();
#endif

        Debug.Log($"[ApplyDungeonRenderingLayer] Restored '{name}' to {defaultName}. Renderers {rChanged}/{rTotal}, Lights {lChanged}/{lTotal}");
    }
}
