using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public sealed class NavBakeInvisibleRenderer : MonoBehaviour
{
    private void OnEnable()
    {
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    private void Apply()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!renderer) continue;

            renderer.forceRenderingOff = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
