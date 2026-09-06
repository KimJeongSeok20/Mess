using System.Reflection;
using UnityEditor;
using UnityEngine;

internal static class PlayerVitalsHudPreviewMenu
{
    private const string MenuRoot = "Tools/StillWorking/UI/Vitals Preview/";
    private static readonly MethodInfo SetCurrentStaminaMethod = typeof(PlayerVitals).GetMethod(
        "SetCurrentStamina",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo RegenCooldownField = typeof(PlayerVitals).GetField(
        "_regenCooldown",
        BindingFlags.Instance | BindingFlags.NonPublic);

    [MenuItem(MenuRoot + "Full (100 / 100)")]
    private static void PreviewFull()
    {
        ApplyPreview(1f, 1f);
    }

    [MenuItem(MenuRoot + "Wounded (72 / 43)")]
    private static void PreviewWounded()
    {
        ApplyPreview(0.72f, 0.43f);
    }

    [MenuItem(MenuRoot + "Critical (18 / 12)")]
    private static void PreviewCritical()
    {
        ApplyPreview(0.18f, 0.12f);
    }

    [MenuItem(MenuRoot + "Font/1 - Inter Display")]
    private static void PreviewInterDisplay()
    {
        ApplyFont(PlayerVitalsHudFont.InterDisplay);
    }

    [MenuItem(MenuRoot + "Font/2 - Barlow Condensed")]
    private static void PreviewBarlowCondensed()
    {
        ApplyFont(PlayerVitalsHudFont.BarlowCondensed);
    }

    [MenuItem(MenuRoot + "Font/3 - Rajdhani")]
    private static void PreviewRajdhani()
    {
        ApplyFont(PlayerVitalsHudFont.Rajdhani);
    }

    [MenuItem(MenuRoot + "Font/4 - Oxanium")]
    private static void PreviewOxanium()
    {
        ApplyFont(PlayerVitalsHudFont.Oxanium);
    }

    private static void ApplyFont(PlayerVitalsHudFont font)
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[PlayerVitals HUD Preview] Enter Play Mode before changing the HUD font.");
            return;
        }

        PlayerVitalsHud.SetFont(font);
        ApplyPreview(0.72f, 0.43f);
        Debug.Log($"[PlayerVitals HUD Preview] Applied font: {font}.");
    }

    private static void ApplyPreview(float healthNormalized, float staminaNormalized)
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[PlayerVitals HUD Preview] Enter Play Mode before applying a preview state.");
            return;
        }

        PlayerVitals vitals = FindLocalVitals();
        if (vitals == null)
        {
            Debug.LogWarning("[PlayerVitals HUD Preview] No local PlayerVitals with an active camera was found.");
            return;
        }

        vitals.Heal(vitals.MaxHealth);
        int targetHealth = Mathf.Clamp(
            Mathf.RoundToInt(vitals.MaxHealth * healthNormalized),
            1,
            vitals.MaxHealth);
        int damage = vitals.CurrentHealth - targetHealth;
        if (damage > 0)
            vitals.TakeDamage(damage);

        float targetStamina = Mathf.Clamp(vitals.MaxStamina * staminaNormalized, 0f, vitals.MaxStamina);
        SetCurrentStaminaMethod?.Invoke(vitals, new object[] { targetStamina });
        RegenCooldownField?.SetValue(vitals, staminaNormalized < 0.999f ? 999f : 0f);

        Debug.Log(
            $"[PlayerVitals HUD Preview] Applied HP {vitals.CurrentHealth}/{vitals.MaxHealth}, " +
            $"ST {vitals.CurrentStamina:F0}/{vitals.MaxStamina:F0}.");
    }

    private static PlayerVitals FindLocalVitals()
    {
        PlayerVitals[] candidates = Object.FindObjectsByType<PlayerVitals>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < candidates.Length; i++)
        {
            Camera camera = candidates[i].GetComponentInChildren<Camera>(includeInactive: true);
            if (camera != null && camera.isActiveAndEnabled)
                return candidates[i];
        }

        return null;
    }
}
