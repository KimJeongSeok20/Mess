using UnityEngine;

namespace GrokDoorwayLighting
{
    public static class GrokDoorwayMaterialUtility
    {
        public const string ShaderName = "Hidden/GrokDoorwayLighting/GiAdd";
        public const string OverlayName = "GrokGiAdd";

        public static Shader FindShader()
        {
            return Shader.Find(ShaderName);
        }

        public static Material CreateOverlayMaterial(Material source)
        {
            Shader shader = FindShader();
            if (shader == null)
                return null;

            var material = new Material(shader)
            {
                name = source != null ? source.name + "_GrokGiAdd" : "GrokGiAdd",
                hideFlags = HideFlags.HideAndDontSave
            };

            Texture albedo = FirstTexture(source, "_BaseMap", "_MainTex", "_Basecolor");
            material.SetTexture("_BaseMap", albedo);
            material.SetTextureScale("_BaseMap", FirstTextureScale(source, "_BaseMap", "_MainTex", "_Basecolor"));
            material.SetTextureOffset("_BaseMap", FirstTextureOffset(source, "_BaseMap", "_MainTex", "_Basecolor"));
            CopyColor(source, material, "_BaseColor", FirstColor(source, Color.white, "_BaseColor", "_Color"));
            CopyFloat(source, material, "_Metallic", 0f);
            CopyFloat(source, material, "_Smoothness", 0.5f);
            CopyFloat(source, material, "_BumpScale", 1f);
            Texture bump = FirstTexture(source, "_BumpMap", "_Normal");
            material.SetTexture("_BumpMap", bump);
            if (bump != null)
            {
                material.SetTextureScale("_BumpMap", FirstTextureScale(source, "_BumpMap", "_Normal"));
                material.SetTextureOffset("_BumpMap", FirstTextureOffset(source, "_BumpMap", "_Normal"));
            }
            CopyTexture(source, material, "_MetallicGlossMap", "_MetallicSmoothness");
            CopyTexture(source, material, "_ColorMask");
            bool gabro = source != null &&
                         (source.IsKeywordEnabled("_COLORCHANGE_ON") ||
                          (source.HasProperty("_ColorChange") && source.GetFloat("_ColorChange") > 0.5f));
            material.SetFloat("_UseGabroColorChange", gabro ? 1f : 0f);
            CopyColor(source, material, "_Custom_Color", Color.white);
            CopyFloat(source, material, "_ColorChange", 0f);
            CopyFloat(source, material, "_HueShiftOnly", 0f);
            CopyFloat(source, material, "_HueShift", 0f);
            return material;
        }

        public static void ApplyDoorway(
            Material material,
            in GrokDoorwayMath.DoorwayFrame door,
            Texture portal,
            float spillScale,
            float bounceReflectance,
            float bounceRange,
            float bounceScale,
            float transferMode,
            float revealScale,
            bool enabled)
        {
            if (material == null)
                return;

            material.SetTexture("_PortalRadiance", portal);
            material.SetFloat("_SpillScale", spillScale);
            material.SetFloat("_BounceReflectance", bounceReflectance);
            material.SetFloat("_BounceRange", bounceRange);
            material.SetFloat("_BounceScale", bounceScale);
            material.SetFloat("_TransferMode", transferMode);
            material.SetFloat("_RevealScale", revealScale);
            material.SetVector("_DoorwayPositionWS", door.position);
            material.SetVector("_DoorwayInwardWS", door.inward);
            material.SetVector("_DoorwayRightWS", new Vector4(door.right.x, door.right.y, door.right.z, door.halfWidth));
            material.SetVector("_DoorwayUpWS", new Vector4(door.up.x, door.up.y, door.up.z, door.halfHeight));
            material.SetVector("_FadeParams", new Vector4(
                door.blendDepth,
                door.lateralFade,
                door.verticalPadding,
                door.jambAllowance));
            material.SetFloat("_Enabled", enabled ? 1f : 0f);
        }

        private static void CopyTexture(Material source, Material dest, params string[] names)
        {
            dest.SetTexture(names[0], FirstTexture(source, names));
        }

        private static void CopyColor(Material source, Material dest, string name, Color fallback)
        {
            dest.SetColor(name, FirstColor(source, fallback, name));
        }

        private static void CopyFloat(Material source, Material dest, string name, float fallback)
        {
            dest.SetFloat(name, source != null && source.HasProperty(name) ? source.GetFloat(name) : fallback);
        }

        private static Texture FirstTexture(Material source, params string[] names)
        {
            if (source == null)
                return null;
            for (int i = 0; i < names.Length; i++)
            {
                if (!source.HasProperty(names[i]))
                    continue;
                Texture texture = source.GetTexture(names[i]);
                if (texture != null)
                    return texture;
            }

            return null;
        }

        private static Vector2 FirstTextureScale(Material source, params string[] names)
        {
            if (source == null)
                return Vector2.one;
            for (int i = 0; i < names.Length; i++)
            {
                if (source.HasProperty(names[i]))
                    return source.GetTextureScale(names[i]);
            }

            return Vector2.one;
        }

        private static Vector2 FirstTextureOffset(Material source, params string[] names)
        {
            if (source == null)
                return Vector2.zero;
            for (int i = 0; i < names.Length; i++)
            {
                if (source.HasProperty(names[i]))
                    return source.GetTextureOffset(names[i]);
            }

            return Vector2.zero;
        }

        private static Color FirstColor(Material source, Color fallback, params string[] names)
        {
            if (source == null)
                return fallback;
            for (int i = 0; i < names.Length; i++)
            {
                if (source.HasProperty(names[i]))
                    return source.GetColor(names[i]);
            }

            return fallback;
        }
    }
}
