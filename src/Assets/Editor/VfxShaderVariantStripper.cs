using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Build-time shader variant stripper for the particle / decal VFX packs (Vefects blood + fire).
/// Those Amplify-generated shaders declare every URP keyword set, so each one compiles ~2,300
/// variants per pass (about 40 minutes per shader on this machine). Particles and blood decals
/// never use baked lighting, DOTS instancing, LOD cross-fade or debug displays, so those axes
/// are dropped here. Everything else (fog, real-time shadows, additional lights) is kept.
/// The same axes are dropped for the IDA_Skin character shader (see ShaderNameMarkers).
/// </summary>
public sealed class VfxShaderVariantStripper : IPreprocessShaders
{
    // IDA_Skin is the character skin graph: it exceeds the 16-sampler limit of ps_4_0 only in its
    // lightmap variants, and characters are never lightmapped, so those variants are dropped too.
    private static readonly string[] ShaderNameMarkers = { "Vefects", "IDA_Skin" };

    private static readonly string[] StrippedKeywords =
    {
        "LIGHTMAP_ON", "DIRLIGHTMAP_COMBINED", "DYNAMICLIGHTMAP_ON", "LIGHTMAP_SHADOW_MIXING",
        "SHADOWS_SHADOWMASK", "USE_LEGACY_LIGHTMAPS",
        "PROBE_VOLUMES_L1", "PROBE_VOLUMES_L2", "EVALUATE_SH_MIXED", "EVALUATE_SH_VERTEX",
        "DOTS_INSTANCING_ON", "LOD_FADE_CROSSFADE", "DEBUG_DISPLAY",
        "_LIGHT_LAYERS", "_LIGHT_COOKIES", "_WRITE_RENDERING_LAYERS",
        "_DBUFFER_MRT1", "_DBUFFER_MRT2", "_DBUFFER_MRT3",
    };

    private static int s_totalStripped;
    private static int s_totalKept;

    public int callbackOrder => 10;

    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        if (shader == null || !IsVfxShader(shader.name))
            return;

        int before = data.Count;
        for (int i = data.Count - 1; i >= 0; i--)
        {
            if (HasStrippedKeyword(shader, data[i].shaderKeywordSet))
                data.RemoveAt(i);
        }

        int stripped = before - data.Count;
        s_totalStripped += stripped;
        s_totalKept += data.Count;

        if (stripped > 0)
            Debug.Log($"[VfxShaderVariantStripper] {shader.name} {snippet.passName}: kept {data.Count}, stripped {stripped} (running total stripped={s_totalStripped}, kept={s_totalKept})");
    }

    private static bool IsVfxShader(string shaderName)
    {
        for (int i = 0; i < ShaderNameMarkers.Length; i++)
        {
            if (shaderName.IndexOf(ShaderNameMarkers[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static bool HasStrippedKeyword(Shader shader, ShaderKeywordSet keywordSet)
    {
        for (int i = 0; i < StrippedKeywords.Length; i++)
        {
            var keyword = new ShaderKeyword(shader, StrippedKeywords[i]);
            if (keyword.IsValid() && keywordSet.IsEnabled(keyword))
                return true;
        }

        return false;
    }
}
