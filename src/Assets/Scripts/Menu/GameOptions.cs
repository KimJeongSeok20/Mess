using UnityEngine;

/// <summary>Local user preferences, independent of saved game progress.</summary>
public static class GameOptions
{
    private const string KeyPrefix = "StillWorking.Options.";

    public const float MinimumSensitivity = 0.1f;
    public const float MaximumSensitivity = 3f;
    public const int MinimumFrameRate = 30;
    public const int MaximumFrameRate = 240;

    private static float _masterVolume = 1f;
    private static float _mouseSensitivityMultiplier = 1f;
    private static int _qualityLevel;
    private static int _frameRateLimit = 60;

    public static float MasterVolume
    {
        get => _masterVolume;
        set => _masterVolume = Mathf.Clamp01(FiniteOrDefault(value, 1f));
    }

    public static float MouseSensitivityMultiplier
    {
        get => _mouseSensitivityMultiplier;
        set => _mouseSensitivityMultiplier = Mathf.Clamp(
            FiniteOrDefault(value, 1f), MinimumSensitivity, MaximumSensitivity);
    }

    public static bool Fullscreen { get; set; } = true;

    public static int QualityLevel
    {
        get => _qualityLevel;
        set => _qualityLevel = Mathf.Clamp(value, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
    }

    public static bool VSync { get; set; } = true;

    /// <summary>Zero disables the frame limit. VSync takes precedence when enabled.</summary>
    public static int FrameRateLimit
    {
        get => _frameRateLimit;
        set => _frameRateLimit = value <= 0 ? 0 : Mathf.Clamp(value, MinimumFrameRate, MaximumFrameRate);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        Load();
        Apply();
    }

    /// <summary>Reads and validates preferences without applying or writing them.</summary>
    public static void Load()
    {
        MasterVolume = PlayerPrefs.GetFloat(KeyPrefix + nameof(MasterVolume), 1f);
        MouseSensitivityMultiplier = PlayerPrefs.GetFloat(KeyPrefix + nameof(MouseSensitivityMultiplier), 1f);
        Fullscreen = PlayerPrefs.GetInt(KeyPrefix + nameof(Fullscreen), 1) == 1;
        QualityLevel = PlayerPrefs.GetInt(KeyPrefix + nameof(QualityLevel), 0);
        VSync = PlayerPrefs.GetInt(KeyPrefix + nameof(VSync), 1) == 1;
        FrameRateLimit = PlayerPrefs.GetInt(KeyPrefix + nameof(FrameRateLimit), 60);
    }

    /// <summary>Applies the current values. Call Save to persist them.</summary>
    public static void Apply()
    {
        AudioListener.volume = MasterVolume;

        if (Screen.fullScreen != Fullscreen)
            Screen.fullScreen = Fullscreen;

        if (QualitySettings.GetQualityLevel() != QualityLevel)
            QualitySettings.SetQualityLevel(QualityLevel, true);

        // Quality presets also contain vSyncCount, so apply the user's choice last.
        ApplyFramePacing();
    }

    public static void ApplyFramePacing()
    {
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        Application.targetFrameRate = VSync || FrameRateLimit == 0 ? -1 : FrameRateLimit;
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat(KeyPrefix + nameof(MasterVolume), MasterVolume);
        PlayerPrefs.SetFloat(KeyPrefix + nameof(MouseSensitivityMultiplier), MouseSensitivityMultiplier);
        PlayerPrefs.SetInt(KeyPrefix + nameof(Fullscreen), Fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(KeyPrefix + nameof(QualityLevel), QualityLevel);
        PlayerPrefs.SetInt(KeyPrefix + nameof(VSync), VSync ? 1 : 0);
        PlayerPrefs.SetInt(KeyPrefix + nameof(FrameRateLimit), FrameRateLimit);
        PlayerPrefs.Save();
    }

    /// <summary>Restores, applies, and saves the defaults used by this PC game.</summary>
    public static void ResetDefaults()
    {
        MasterVolume = 1f;
        MouseSensitivityMultiplier = 1f;
        Fullscreen = true;
        QualityLevel = 0;
        VSync = true;
        FrameRateLimit = 60;
        Apply();
        Save();
    }

    private static float FiniteOrDefault(float value, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }
}
