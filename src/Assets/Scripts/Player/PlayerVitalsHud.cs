using TMPro;
using UnityEngine;

public enum PlayerVitalsHudFont
{
    InterDisplay,
    BarlowCondensed,
    Rajdhani,
    Oxanium
}

[DisallowMultipleComponent]
public sealed class PlayerVitalsHud : MonoBehaviour
{
    private const float HealthTrailHoldSeconds = 0.28f;
    private const float HealthTrailCatchupPerSecond = 0.42f;
    private const float HealthTraceWidthAt100 = 116f;
    private const float PerkTokenSize = 24f;
    private const float PerkTokenSpacing = 30f;

    private static readonly PlayerPerkKind[] HudPerks =
    {
        PlayerPerkKind.ExtraAirJump,
        PlayerPerkKind.GroundSlam,
        PlayerPerkKind.Adrenaline,
        PlayerPerkKind.SafetyNet,
        PlayerPerkKind.SecondChance,
        PlayerPerkKind.FreeForge,
        PlayerPerkKind.LastStand,
        PlayerPerkKind.TeamInsurance
    };

    private const string InterDisplayFontPath = "Font/InterDisplay-Bold SDF";
    private const string BarlowCondensedFontPath = "UI/Fonts/Vitals/BarlowCondensed-SemiBold SDF";
    private const string RajdhaniFontPath = "UI/Fonts/Vitals/Rajdhani-SemiBold SDF";
    private const string OxaniumFontPath = "UI/Fonts/Vitals/Oxanium-SemiBold SDF";

    private static readonly Color HealthColor = new Color32(239, 50, 58, 255);
    private static readonly Color HealthCriticalColor = new Color32(255, 74, 66, 255);
    private static readonly Color StaminaColor = new Color32(216, 178, 91, 255);
    private static readonly Color StaminaLowColor = new Color32(235, 132, 58, 255);
    private static readonly Color HealthValueColor = new Color32(244, 65, 68, 255);
    private static readonly Color StaminaValueColor = new Color32(226, 222, 207, 255);
    private static readonly Color BatteryColor = new Color32(178, 139, 209, 255);

    private VitalTelemetryRailGraphic _railGraphic;
    private VitalPulseTraceGraphic _healthTrace;
    private VitalPulseTraceGraphic _staminaTrace;
    private VitalTelemetryIconGraphic _healthIcon;
    private VitalTelemetryIconGraphic _staminaIcon;
    private RectTransform _healthIconRect;
    private RectTransform _staminaIconRect;
    private TMP_Text _healthValue;
    private TMP_Text _staminaValue;
    private UnityEngine.CanvasGroup _staminaGroup;
    private RectTransform _batteryRow;
    private UnityEngine.UI.Image _batteryFill;
    private TMP_Text _batteryValue;
    private NetworkDungeonController _dungeon;
    private float _nextBatteryPoll;
    private RectTransform _rootRect;
    private RectTransform _perkRow;
    private RectTransform[] _perkTokens;
    private int _visiblePerkMask = -1;

    private float _healthNormalized = 1f;
    private float _healthTrailNormalized = 1f;
    private int _health;
    private int _maxHealth;
    private float _staminaNormalized = 1f;
    private float _healthTrailHold;
    private float _hitFlash;
    private float _staminaActivity;
    private float _healthPhase;
    private float _staminaPhase;
    private float _staminaAlpha = 0.78f;
    private bool _hasValues;

    public static PlayerVitalsHudFont SelectedFont { get; private set; } = PlayerVitalsHudFont.BarlowCondensed;

    public static PlayerVitalsHud Create(Canvas canvas, Vector2 anchorOffset, Sprite batteryIcon)
    {
        var root = new GameObject("VitalsHUD_PulseTelemetry", typeof(RectTransform), typeof(PlayerVitalsHud));
        var rootRect = root.GetComponent<RectTransform>();
        rootRect.SetParent(canvas.transform, false);
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.anchoredPosition = anchorOffset;
        rootRect.sizeDelta = new Vector2(278f, 198f);

        var hud = root.GetComponent<PlayerVitalsHud>();
        hud.Build(rootRect);
        hud.BuildBattery(rootRect, batteryIcon);
        hud.BuildPerkTokens(rootRect);
        rootRect.SetAsLastSibling();
        return hud;
    }

    public static void SetFont(PlayerVitalsHudFont font)
    {
        SelectedFont = font;

        PlayerVitalsHud[] huds = FindObjectsByType<PlayerVitalsHud>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < huds.Length; i++)
            huds[i].ApplyValueFont();
    }

    public void SetValues(int health, int maxHealth, float stamina, float maxStamina)
    {
        float nextHealth = maxHealth <= 0 ? 0f : Mathf.Clamp01((float)health / maxHealth);
        float nextStamina = maxStamina <= 0f ? 0f : Mathf.Clamp01(stamina / maxStamina);

        if (!_hasValues || maxHealth != _maxHealth)
            ResizeHealthCapacity(maxHealth);

        if (!_hasValues)
        {
            _healthNormalized = nextHealth;
            _healthTrailNormalized = nextHealth;
            _staminaNormalized = nextStamina;
            _hasValues = true;
        }
        else
        {
            // Keep any recent damage trail in health points when a perk changes the maximum.
            if (maxHealth != _maxHealth)
            {
                _healthTrailNormalized = maxHealth <= 0
                    ? 0f
                    : Mathf.Clamp01(_healthTrailNormalized * _maxHealth / maxHealth);
            }

            if (health < Mathf.Min(_health, maxHealth))
            {
                float previousHealth = maxHealth <= 0 ? 0f : Mathf.Clamp01((float)_health / maxHealth);
                _healthTrailNormalized = Mathf.Max(_healthTrailNormalized, previousHealth);
                _healthTrailHold = HealthTrailHoldSeconds;
                _hitFlash = 1f;
            }
            else
            {
                _healthTrailNormalized = Mathf.Max(_healthTrailNormalized, nextHealth);
            }

            if (!Mathf.Approximately(nextStamina, _staminaNormalized))
                _staminaActivity = 1f;

            _healthNormalized = nextHealth;
            _staminaNormalized = nextStamina;
        }

        _health = health;
        _maxHealth = maxHealth;

        if (_healthValue != null)
            _healthValue.SetText("{0}", health);

        if (_staminaValue != null)
            _staminaValue.SetText("{0}", Mathf.CeilToInt(stamina));

        RefreshVisuals();
    }

    public void SetPerks(PlayerPerks perks)
    {
        if (_perkRow == null)
            return;

        int mask = 0;
        for (int i = 0; i < HudPerks.Length; i++)
        {
            if (perks != null && perks.Total(HudPerks[i]) > 0f)
                mask |= 1 << i;
        }

        if (mask == _visiblePerkMask)
            return;

        _visiblePerkMask = mask;
        int visibleCount = 0;
        for (int i = 0; i < _perkTokens.Length; i++)
        {
            bool visible = (mask & (1 << i)) != 0;
            _perkTokens[i].gameObject.SetActive(visible);
            if (visible)
                _perkTokens[i].anchoredPosition = new Vector2(visibleCount++ * PerkTokenSpacing, 0f);
        }

        _perkRow.sizeDelta = new Vector2(Mathf.Max(0f, visibleCount * PerkTokenSpacing - 6f), PerkTokenSize);
        _perkRow.gameObject.SetActive(visibleCount > 0);
        PositionPerkRow();
    }

    private void ResizeHealthCapacity(int maxHealth)
    {
        if (_healthTrace == null || _healthValue == null)
            return;

        float width = HealthTraceWidthAt100 * (maxHealth > 0 ? maxHealth / 100f : 1f);
        RectTransform traceRect = _healthTrace.rectTransform;
        traceRect.sizeDelta = new Vector2(width, traceRect.sizeDelta.y);
        RectTransform valueRect = _healthValue.rectTransform;
        valueRect.anchoredPosition = new Vector2(traceRect.anchoredPosition.x + width + 10f, valueRect.anchoredPosition.y);
        if (_rootRect != null)
            _rootRect.sizeDelta = new Vector2(Mathf.Max(278f, valueRect.anchoredPosition.x + valueRect.sizeDelta.x), _rootRect.sizeDelta.y);
    }

    private void Build(RectTransform root)
    {
        _rootRect = root;
        _railGraphic = CreateRail(root);

        _healthIcon = CreateIcon(
            root,
            "Health_Heart",
            VitalTelemetryIconKind.Heart,
            new Vector2(26f, -33f),
            new Vector2(24f, 23f),
            HealthColor,
            out _healthIconRect);

        _healthTrace = CreateTrace(
            root,
            "Health_PulseTrace",
            VitalPulseTraceMode.Heartbeat,
            new Vector2(55f, -26f),
            new Vector2(HealthTraceWidthAt100, 38f),
            HealthColor);

        _healthValue = CreateValueText(
            root,
            "Health_Value",
            new Vector2(181f, -28f),
            new Vector2(64f, 31f),
            25f,
            HealthValueColor);

        var staminaRow = new GameObject("Stamina_Row", typeof(RectTransform), typeof(UnityEngine.CanvasGroup));
        var staminaRowRect = staminaRow.GetComponent<RectTransform>();
        staminaRowRect.SetParent(root, false);
        staminaRowRect.anchorMin = new Vector2(0f, 1f);
        staminaRowRect.anchorMax = new Vector2(0f, 1f);
        staminaRowRect.pivot = new Vector2(0f, 1f);
        staminaRowRect.anchoredPosition = new Vector2(0f, -73f);
        staminaRowRect.sizeDelta = new Vector2(254f, 54f);
        _staminaGroup = staminaRow.GetComponent<UnityEngine.CanvasGroup>();
        _staminaGroup.alpha = _staminaAlpha;
        _staminaGroup.interactable = false;
        _staminaGroup.blocksRaycasts = false;

        _staminaIcon = CreateIcon(
            staminaRowRect,
            "Stamina_Bolt",
            VitalTelemetryIconKind.Bolt,
            new Vector2(27f, -13f),
            new Vector2(22f, 25f),
            StaminaColor,
            out _staminaIconRect);

        _staminaTrace = CreateTrace(
            staminaRowRect,
            "Stamina_BreathTrace",
            VitalPulseTraceMode.Breath,
            new Vector2(55f, -7f),
            new Vector2(116f, 38f),
            StaminaColor);

        _staminaValue = CreateValueText(
            staminaRowRect,
            "Stamina_Value",
            new Vector2(181f, -9f),
            new Vector2(64f, 31f),
            25f,
            StaminaValueColor);

        ApplyValueFont();
    }

    private void ApplyValueFont()
    {
        TMP_FontAsset font = LoadSelectedFont();
        if (font == null)
            font = Resources.Load<TMP_FontAsset>(InterDisplayFontPath);
        if (font == null)
            font = TMP_Settings.defaultFontAsset;

        if (_healthValue != null)
            _healthValue.font = font;
        if (_staminaValue != null)
            _staminaValue.font = font;
        if (_batteryValue != null)
            _batteryValue.font = font;
    }

    private void BuildBattery(RectTransform root, Sprite icon)
    {
        _batteryRow = new GameObject("DungeonBattery_Row", typeof(RectTransform)).GetComponent<RectTransform>();
        _batteryRow.SetParent(root, false);
        _batteryRow.anchorMin = _batteryRow.anchorMax = _batteryRow.pivot = new Vector2(0f, 1f);
        _batteryRow.anchoredPosition = new Vector2(0f, -126f);
        _batteryRow.sizeDelta = new Vector2(254f, 54f);

        var batteryIcon = CreateBatteryImage(_batteryRow, "Battery_Icon", new Vector2(19f, -2f), new Vector2(38f, 38f), Color.white);
        batteryIcon.sprite = icon;
        batteryIcon.preserveAspect = true;

        var track = CreateBatteryImage(_batteryRow, "Battery_Track", new Vector2(55f, -20f), new Vector2(116f, 3f), new Color(0.70f, 0.55f, 0.82f, 0.18f));
        _batteryFill = CreateBatteryImage(track.rectTransform, "Battery_Fill", Vector2.zero, Vector2.zero, BatteryColor);
        _batteryFill.rectTransform.anchorMin = Vector2.zero;
        _batteryFill.rectTransform.anchorMax = Vector2.one;
        _batteryFill.rectTransform.offsetMin = _batteryFill.rectTransform.offsetMax = Vector2.zero;

        _batteryValue = CreateValueText(_batteryRow, "Battery_Value",
            new Vector2(181f, -6f), new Vector2(64f, 31f), 25f, BatteryColor);
        _batteryValue.font = _staminaValue.font;

        RefreshBattery();
    }

    private void BuildPerkTokens(RectTransform root)
    {
        _perkRow = new GameObject("Perk_Tokens", typeof(RectTransform)).GetComponent<RectTransform>();
        _perkRow.SetParent(root, false);
        _perkRow.anchorMin = _perkRow.anchorMax = _perkRow.pivot = new Vector2(0f, 1f);
        _perkTokens = new RectTransform[HudPerks.Length];

        for (int i = 0; i < HudPerks.Length; i++)
        {
            var token = new GameObject("Perk_" + HudPerks[i], typeof(RectTransform), typeof(CanvasRenderer), typeof(PerkTokenGraphic));
            RectTransform rect = token.GetComponent<RectTransform>();
            rect.SetParent(_perkRow, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(PerkTokenSize, PerkTokenSize);
            token.GetComponent<PerkTokenGraphic>().Configure(HudPerks[i]);
            token.SetActive(false);
            _perkTokens[i] = rect;
        }

        _perkRow.gameObject.SetActive(false);
        PositionPerkRow();
    }

    private void PositionPerkRow()
    {
        if (_perkRow == null)
            return;

        bool batteryVisible = _batteryRow != null && _batteryRow.gameObject.activeSelf;
        _perkRow.anchoredPosition = new Vector2(26f, batteryVisible ? -180f : -127f);
        if (_rootRect != null)
            _rootRect.sizeDelta = new Vector2(_rootRect.sizeDelta.x, batteryVisible && _perkRow.gameObject.activeSelf ? 212f : 198f);
    }

    private static UnityEngine.UI.Image CreateBatteryImage(RectTransform parent, string name, Vector2 position, Vector2 size, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<UnityEngine.UI.Image>();
        var rect = image.rectTransform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private void RefreshBattery()
    {
        // The dungeon is spawned independently of the owner HUD; retry only while absent.
        if (_dungeon == null)
            _dungeon = FindAnyObjectByType<NetworkDungeonController>();

        bool ready = _dungeon != null && _dungeon.IsLocalDungeonReady;
        if (_batteryRow.gameObject.activeSelf != ready)
        {
            _batteryRow.gameObject.SetActive(ready);
            PositionPerkRow();
        }
        if (!ready)
            return;

        float charge = Mathf.Clamp01(_dungeon.PowerNormalized);
        _batteryFill.rectTransform.anchorMax = new Vector2(charge, 1f);
        _batteryValue.SetText("{0}", Mathf.CeilToInt(charge * 100f));
    }

    private static TMP_FontAsset LoadSelectedFont()
    {
        string resourcePath = SelectedFont switch
        {
            PlayerVitalsHudFont.BarlowCondensed => BarlowCondensedFontPath,
            PlayerVitalsHudFont.Rajdhani => RajdhaniFontPath,
            PlayerVitalsHudFont.Oxanium => OxaniumFontPath,
            _ => InterDisplayFontPath
        };

        return Resources.Load<TMP_FontAsset>(resourcePath);
    }

    private void Update()
    {
        if (_batteryRow != null && Time.unscaledTime >= _nextBatteryPoll)
        {
            _nextBatteryPoll = Time.unscaledTime + (_dungeon == null ? 1f : 0.25f);
            RefreshBattery();
        }

        if (!_hasValues)
            return;

        float deltaTime = Time.unscaledDeltaTime;

        if (_healthTrailHold > 0f)
        {
            _healthTrailHold = Mathf.Max(0f, _healthTrailHold - deltaTime);
        }
        else if (_healthTrailNormalized > _healthNormalized)
        {
            _healthTrailNormalized = Mathf.MoveTowards(
                _healthTrailNormalized,
                _healthNormalized,
                HealthTrailCatchupPerSecond * deltaTime);
        }
        else
        {
            _healthTrailNormalized = _healthNormalized;
        }

        _hitFlash = Mathf.MoveTowards(_hitFlash, 0f, 3.4f * deltaTime);
        _staminaActivity = Mathf.MoveTowards(_staminaActivity, 0f, 0.72f * deltaTime);

        float healthCyclesPerSecond = Mathf.Lerp(0.92f, 1.62f, 1f - _healthNormalized);
        float staminaCyclesPerSecond = 0.34f + _staminaActivity * 0.82f + (1f - _staminaNormalized) * 0.18f;
        _healthPhase = Mathf.Repeat(_healthPhase + deltaTime * healthCyclesPerSecond, 1f);
        _staminaPhase = Mathf.Repeat(_staminaPhase + deltaTime * staminaCyclesPerSecond, 1f);

        float healthBeat = EvaluateHeartbeat(_healthPhase);
        float criticalPulse = _healthNormalized <= 0.25f
            ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5.4f)
            : 0f;
        float staminaBreath = 0.5f + 0.5f * Mathf.Sin(_staminaPhase * Mathf.PI * 2f);

        if (_healthIconRect != null)
        {
            float scale = 1f + healthBeat * 0.105f + _hitFlash * 0.075f;
            _healthIconRect.localScale = Vector3.one * scale;
        }

        if (_staminaIconRect != null)
        {
            float scale = 1f + staminaBreath * (0.025f + _staminaActivity * 0.025f);
            _staminaIconRect.localScale = Vector3.one * scale;
        }

        float targetStaminaAlpha = _staminaNormalized < 0.995f || _staminaActivity > 0.01f ? 1f : 0.78f;
        _staminaAlpha = Mathf.MoveTowards(_staminaAlpha, targetStaminaAlpha, 2.8f * deltaTime);
        if (_staminaGroup != null)
            _staminaGroup.alpha = _staminaAlpha;

        if (_healthValue != null)
            _healthValue.color = Color.Lerp(HealthValueColor, HealthCriticalColor, criticalPulse * 0.65f);

        if (_staminaValue != null)
            _staminaValue.color = Color.Lerp(StaminaValueColor, StaminaLowColor, _staminaNormalized <= 0.18f ? staminaBreath * 0.55f : 0f);

        RefreshVisuals();
    }

    private void RefreshVisuals()
    {
        float criticalPulse = _healthNormalized <= 0.25f
            ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5.4f)
            : 0f;
        float staminaLowPulse = _staminaNormalized <= 0.18f
            ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.2f)
            : 0f;

        _railGraphic?.SetPhase(Mathf.Repeat(Time.unscaledTime * 0.16f, 1f), _hitFlash);
        _healthTrace?.SetState(
            _healthNormalized,
            _healthTrailNormalized,
            _healthPhase,
            _hitFlash,
            criticalPulse);
        _staminaTrace?.SetState(
            _staminaNormalized,
            _staminaNormalized,
            _staminaPhase,
            _staminaActivity,
            staminaLowPulse);
        _healthIcon?.SetIntensity(Mathf.Max(_hitFlash, criticalPulse * 0.55f));
        _staminaIcon?.SetIntensity(Mathf.Max(_staminaActivity * 0.45f, staminaLowPulse * 0.55f));
    }

    private static float EvaluateHeartbeat(float phase)
    {
        float first = Mathf.Exp(-Mathf.Pow((phase - 0.08f) / 0.036f, 2f));
        float second = Mathf.Exp(-Mathf.Pow((phase - 0.22f) / 0.052f, 2f)) * 0.58f;
        return Mathf.Clamp01(first + second);
    }

    private static VitalTelemetryRailGraphic CreateRail(RectTransform parent)
    {
        var railObject = new GameObject(
            "Telemetry_Rail",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(VitalTelemetryRailGraphic));
        var rect = railObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(14f, -4f);
        rect.sizeDelta = new Vector2(8f, 142f);

        var graphic = railObject.GetComponent<VitalTelemetryRailGraphic>();
        graphic.raycastTarget = false;
        return graphic;
    }

    private static VitalTelemetryIconGraphic CreateIcon(
        RectTransform parent,
        string objectName,
        VitalTelemetryIconKind iconKind,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        Color color,
        out RectTransform iconRect)
    {
        var iconObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(VitalTelemetryIconGraphic),
            typeof(UnityEngine.UI.Shadow));
        iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.SetParent(parent, false);
        iconRect.anchorMin = new Vector2(0f, 1f);
        iconRect.anchorMax = new Vector2(0f, 1f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = anchoredPosition + sizeDelta * new Vector2(0.5f, -0.5f);
        iconRect.sizeDelta = sizeDelta;

        var graphic = iconObject.GetComponent<VitalTelemetryIconGraphic>();
        graphic.Configure(iconKind, color);
        graphic.raycastTarget = false;

        var shadow = iconObject.GetComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = new Color(color.r, color.g, color.b, 0.28f);
        shadow.effectDistance = new Vector2(1f, -1f);
        shadow.useGraphicAlpha = true;
        return graphic;
    }

    private static VitalPulseTraceGraphic CreateTrace(
        RectTransform parent,
        string objectName,
        VitalPulseTraceMode mode,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        Color color)
    {
        var traceObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(VitalPulseTraceGraphic));
        var rect = traceObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        var graphic = traceObject.GetComponent<VitalPulseTraceGraphic>();
        graphic.Configure(mode, color);
        graphic.raycastTarget = false;
        return graphic;
    }

    private static TextMeshProUGUI CreateValueText(
        RectTransform parent,
        string objectName,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        float fontSize,
        Color color)
    {
        var textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI),
            typeof(UnityEngine.UI.Shadow));
        var rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        var text = textObject.GetComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;
        text.text = "100";
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Normal;
        text.fontWeight = FontWeight.Medium;
        text.characterSpacing = 1f;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.color = color;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;

        var shadow = textObject.GetComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = new Color32(0, 0, 0, 205);
        shadow.effectDistance = new Vector2(1f, -1f);
        shadow.useGraphicAlpha = true;
        return text;
    }
}

internal enum VitalPulseTraceMode
{
    Heartbeat,
    Breath
}

internal sealed class VitalPulseTraceGraphic : UnityEngine.UI.MaskableGraphic
{
    private const int SampleCount = 56;

    private VitalPulseTraceMode _mode;
    private Color _activeColor = Color.white;
    private float _current = 1f;
    private float _trail = 1f;
    private float _phase;
    private float _activity;
    private float _lowPulse;

    public void Configure(VitalPulseTraceMode mode, Color activeColor)
    {
        _mode = mode;
        _activeColor = activeColor;
        SetVerticesDirty();
    }

    public void SetState(float current, float trail, float phase, float activity, float lowPulse)
    {
        _current = Mathf.Clamp01(current);
        _trail = Mathf.Clamp01(trail);
        _phase = Mathf.Repeat(phase, 1f);
        _activity = Mathf.Clamp01(activity);
        _lowPulse = Mathf.Clamp01(lowPulse);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        Rect rect = GetPixelAdjustedRect();
        Color trailColor = _mode == VitalPulseTraceMode.Heartbeat
            ? new Color32(117, 35, 39, 205)
            : new Color32(112, 89, 48, 185);
        Color active = Color.Lerp(
            _activeColor,
            _mode == VitalPulseTraceMode.Heartbeat
                ? new Color32(255, 101, 91, 255)
                : new Color32(244, 203, 107, 255),
            Mathf.Max(_activity * 0.52f, _lowPulse * 0.45f));

        if (_mode == VitalPulseTraceMode.Heartbeat)
        {
            Color capacityColor = new Color32(99, 35, 41, 155);
            AddTraceRange(vertexHelper, rect, 0f, 1f, 0.85f, capacityColor, 0f);
            AddLine(vertexHelper,
                new Vector2(rect.xMax, rect.center.y - 3f),
                new Vector2(rect.xMax, rect.center.y + 3f),
                0.85f, capacityColor);
        }

        bool hasHealthTrail = _mode == VitalPulseTraceMode.Heartbeat && _trail > _current + 0.0001f;
        AddTraceRange(
            vertexHelper,
            rect,
            0f,
            _current,
            1.55f + _activity * 0.55f,
            active,
            hasHealthTrail ? 0f : 8f);

        if (hasHealthTrail)
        {
            AddTraceRange(
                vertexHelper,
                rect,
                _current,
                _trail,
                1.05f,
                trailColor,
                8f);
        }

        float cursorT = Mathf.Repeat(_phase + (_mode == VitalPulseTraceMode.Heartbeat ? 0.1f : 0.35f), 1f);
        if (cursorT <= _current)
        {
            Vector2 cursor = EvaluatePoint(rect, cursorT);
            float radius = 1.7f + _activity * 1.2f;
            AddDiamond(vertexHelper, cursor, radius, active);
        }
    }

    private void AddTraceRange(
        UnityEngine.UI.VertexHelper vertexHelper,
        Rect rect,
        float start,
        float end,
        float width,
        Color color,
        float endFadePixels)
    {
        start = Mathf.Clamp01(start);
        end = Mathf.Clamp01(end);
        if (end <= start + 0.0001f)
            return;

        int steps = Mathf.Max(1, Mathf.CeilToInt((end - start) * SampleCount));
        float fadeRange = endFadePixels <= 0f
            ? 0f
            : endFadePixels / Mathf.Max(1f, rect.width);
        Vector2 previous = EvaluatePoint(rect, start);

        for (int i = 1; i <= steps; i++)
        {
            float previousT = Mathf.Lerp(start, end, (i - 1f) / steps);
            float t = Mathf.Lerp(start, end, i / (float)steps);
            float middle = (previousT + t) * 0.5f;
            float fade = fadeRange <= 0f
                ? 1f
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((end - middle) / fadeRange));

            Color segmentColor = color;
            segmentColor.a *= Mathf.Lerp(0.12f, 1f, fade);
            float segmentWidth = width * Mathf.Lerp(0.52f, 1f, fade);
            Vector2 current = EvaluatePoint(rect, t);
            AddLine(vertexHelper, previous, current, segmentWidth, segmentColor);
            previous = current;
        }
    }

    private Vector2 EvaluatePoint(Rect rect, float t)
    {
        float patternPhase = Mathf.Repeat(t - _phase, 1f);
        float shape = _mode == VitalPulseTraceMode.Heartbeat
            ? EvaluateHeartPattern(patternPhase)
            : EvaluateBreathPattern(patternPhase);
        float amplitude = rect.height * (_mode == VitalPulseTraceMode.Heartbeat ? 0.42f : 0.31f);
        return new Vector2(
            Mathf.Lerp(rect.xMin, rect.xMax, t),
            rect.center.y + shape * amplitude);
    }

    private static float EvaluateHeartPattern(float phase)
    {
        float value = 0f;
        value += Triangle(phase, 0.105f, 0.025f) * 0.16f;
        value -= Triangle(phase, 0.158f, 0.024f) * 0.34f;
        value += Triangle(phase, 0.205f, 0.017f) * 1f;
        value -= Triangle(phase, 0.246f, 0.025f) * 0.62f;
        value += Triangle(phase, 0.314f, 0.042f) * 0.2f;
        value += Triangle(phase, 0.625f, 0.018f) * 0.78f;
        value -= Triangle(phase, 0.661f, 0.024f) * 0.48f;
        value += Triangle(phase, 0.716f, 0.038f) * 0.14f;
        return Mathf.Clamp(value, -1f, 1f);
    }

    private static float EvaluateBreathPattern(float phase)
    {
        float baseWave = Mathf.Sin(phase * Mathf.PI * 2f) * 0.16f;
        float pulse = Triangle(phase, 0.22f, 0.035f) * 0.64f;
        float recoil = Triangle(phase, 0.285f, 0.04f) * -0.42f;
        float second = Triangle(phase, 0.62f, 0.055f) * 0.36f;
        return Mathf.Clamp(baseWave + pulse + recoil + second, -1f, 1f);
    }

    private static float Triangle(float value, float center, float halfWidth)
    {
        float distance = Mathf.Abs(Mathf.DeltaAngle(value * 360f, center * 360f)) / 360f;
        return Mathf.Clamp01(1f - distance / Mathf.Max(0.0001f, halfWidth));
    }

    private static void AddLine(
        UnityEngine.UI.VertexHelper vertexHelper,
        Vector2 from,
        Vector2 to,
        float width,
        Color color)
    {
        Vector2 direction = to - from;
        if (direction.sqrMagnitude <= 0.000001f)
            return;

        Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (width * 0.5f);
        int start = vertexHelper.currentVertCount;
        AddVertex(vertexHelper, from - normal, color);
        AddVertex(vertexHelper, from + normal, color);
        AddVertex(vertexHelper, to + normal, color);
        AddVertex(vertexHelper, to - normal, color);
        vertexHelper.AddTriangle(start, start + 1, start + 2);
        vertexHelper.AddTriangle(start, start + 2, start + 3);
    }

    private static void AddDiamond(
        UnityEngine.UI.VertexHelper vertexHelper,
        Vector2 center,
        float radius,
        Color color)
    {
        int start = vertexHelper.currentVertCount;
        AddVertex(vertexHelper, center + Vector2.up * radius, color);
        AddVertex(vertexHelper, center + Vector2.right * radius, color);
        AddVertex(vertexHelper, center + Vector2.down * radius, color);
        AddVertex(vertexHelper, center + Vector2.left * radius, color);
        vertexHelper.AddTriangle(start, start + 1, start + 2);
        vertexHelper.AddTriangle(start, start + 2, start + 3);
    }

    private static void AddVertex(UnityEngine.UI.VertexHelper vertexHelper, Vector2 position, Color color)
    {
        UnityEngine.UIVertex vertex = UnityEngine.UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = color;
        vertex.uv0 = Vector2.zero;
        vertexHelper.AddVert(vertex);
    }
}

internal enum VitalTelemetryIconKind
{
    Heart,
    Bolt
}

internal sealed class VitalTelemetryIconGraphic : UnityEngine.UI.MaskableGraphic
{
    private VitalTelemetryIconKind _kind;
    private Color _baseColor = Color.white;
    private float _intensity;

    public void Configure(VitalTelemetryIconKind kind, Color baseColor)
    {
        _kind = kind;
        _baseColor = baseColor;
        SetVerticesDirty();
    }

    public void SetIntensity(float intensity)
    {
        _intensity = Mathf.Clamp01(intensity);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        Rect rect = GetPixelAdjustedRect();
        Color color = Color.Lerp(_baseColor, Color.white, _intensity * 0.28f);

        if (_kind == VitalTelemetryIconKind.Heart)
            DrawHeart(vertexHelper, rect, color);
        else
            DrawBolt(vertexHelper, rect, color);
    }

    private static void DrawHeart(UnityEngine.UI.VertexHelper vertexHelper, Rect rect, Color color)
    {
        const int pointCount = 28;
        Vector2 center = new Vector2(rect.center.x, rect.center.y - rect.height * 0.04f);
        float scaleX = rect.width / 34f;
        float scaleY = rect.height / 31f;

        int centerIndex = vertexHelper.currentVertCount;
        AddVertex(vertexHelper, center, color);
        for (int i = 0; i <= pointCount; i++)
        {
            float angle = i / (float)pointCount * Mathf.PI * 2f;
            float sine = Mathf.Sin(angle);
            float x = 16f * sine * sine * sine;
            float y =
                13f * Mathf.Cos(angle)
                - 5f * Mathf.Cos(2f * angle)
                - 2f * Mathf.Cos(3f * angle)
                - Mathf.Cos(4f * angle);
            AddVertex(vertexHelper, center + new Vector2(x * scaleX, y * scaleY), color);
        }

        for (int i = 0; i < pointCount; i++)
            vertexHelper.AddTriangle(centerIndex, centerIndex + i + 1, centerIndex + i + 2);
    }

    private static void DrawBolt(UnityEngine.UI.VertexHelper vertexHelper, Rect rect, Color color)
    {
        Vector2[] points =
        {
            new(Mathf.Lerp(rect.xMin, rect.xMax, 0.58f), rect.yMax),
            new(Mathf.Lerp(rect.xMin, rect.xMax, 0.22f), Mathf.Lerp(rect.yMin, rect.yMax, 0.51f)),
            new(Mathf.Lerp(rect.xMin, rect.xMax, 0.46f), Mathf.Lerp(rect.yMin, rect.yMax, 0.51f)),
            new(Mathf.Lerp(rect.xMin, rect.xMax, 0.34f), rect.yMin),
            new(Mathf.Lerp(rect.xMin, rect.xMax, 0.80f), Mathf.Lerp(rect.yMin, rect.yMax, 0.60f)),
            new(Mathf.Lerp(rect.xMin, rect.xMax, 0.55f), Mathf.Lerp(rect.yMin, rect.yMax, 0.60f))
        };

        int start = vertexHelper.currentVertCount;
        for (int i = 0; i < points.Length; i++)
            AddVertex(vertexHelper, points[i], color);
        vertexHelper.AddTriangle(start, start + 1, start + 2);
        vertexHelper.AddTriangle(start, start + 2, start + 5);
        vertexHelper.AddTriangle(start + 2, start + 3, start + 4);
        vertexHelper.AddTriangle(start + 2, start + 4, start + 5);
    }

    private static void AddVertex(UnityEngine.UI.VertexHelper vertexHelper, Vector2 position, Color color)
    {
        UnityEngine.UIVertex vertex = UnityEngine.UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = color;
        vertex.uv0 = Vector2.zero;
        vertexHelper.AddVert(vertex);
    }
}

internal sealed class VitalTelemetryRailGraphic : UnityEngine.UI.MaskableGraphic
{
    private const int DashCount = 13;
    private float _phase;
    private float _impact;

    public void SetPhase(float phase, float impact)
    {
        _phase = Mathf.Repeat(phase, 1f);
        _impact = Mathf.Clamp01(impact);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        Rect rect = GetPixelAdjustedRect();
        int activeDash = Mathf.FloorToInt(_phase * DashCount) % DashCount;

        for (int i = 0; i < DashCount; i++)
        {
            float t = i / (float)(DashCount - 1);
            float y = Mathf.Lerp(rect.yMax - 2f, rect.yMin + 2f, t);
            float proximity = 1f - Mathf.Clamp01(Mathf.Abs(i - activeDash) / 2f);
            Color color = Color.Lerp(
                new Color32(83, 88, 87, 118),
                new Color32(206, 211, 205, 225),
                Mathf.Max(proximity * 0.62f, _impact * (1f - t) * 0.7f));
            float width = i % 4 == 0 ? 4f : 3f;
            AddQuad(vertexHelper, rect.center.x - width * 0.5f, y - 1f, width, 2f, color);
        }
    }

    private static void AddQuad(
        UnityEngine.UI.VertexHelper vertexHelper,
        float x,
        float y,
        float width,
        float height,
        Color color)
    {
        int start = vertexHelper.currentVertCount;
        AddVertex(vertexHelper, new Vector2(x, y), color);
        AddVertex(vertexHelper, new Vector2(x, y + height), color);
        AddVertex(vertexHelper, new Vector2(x + width, y + height), color);
        AddVertex(vertexHelper, new Vector2(x + width, y), color);
        vertexHelper.AddTriangle(start, start + 1, start + 2);
        vertexHelper.AddTriangle(start, start + 2, start + 3);
    }

    private static void AddVertex(UnityEngine.UI.VertexHelper vertexHelper, Vector2 position, Color color)
    {
        UnityEngine.UIVertex vertex = UnityEngine.UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = color;
        vertex.uv0 = Vector2.zero;
        vertexHelper.AddVert(vertex);
    }
}
