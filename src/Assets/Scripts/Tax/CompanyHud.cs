using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen eviction notice shared by the tax-machine callers.
/// Routine company information is displayed in the inventory and player vitals instead.
/// </summary>
[DisallowMultipleComponent]
public sealed class CompanyHud : MonoBehaviour
{
    private const string RootName = "CompanyHudCanvas";
    private const string FontPath = "UI/Fonts/Vitals/BarlowCondensed-SemiBold SDF";

    private static CompanyHud _instance;

    private TMP_FontAsset _font;
    private Sprite _white;
    private GameObject _evictionRoot;
    private TextMeshProUGUI _evictionTitle;
    private TextMeshProUGUI _evictionBody;
    private Coroutine _evictionRoutine;

    public static CompanyHud GetOrCreate()
    {
        if (_instance != null)
            return _instance;

        var root = new GameObject(RootName);
        DontDestroyOnLoad(root);
        _instance = root.AddComponent<CompanyHud>();
        _instance.Build();
        return _instance;
    }

    public static void DestroyIfExists()
    {
        if (_instance == null)
            return;

        Destroy(_instance.gameObject);
        _instance = null;
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    public void SetVisible(bool visible)
    {
        // Compatibility for existing tax-machine callers; the routine ledger was removed.
        // Eviction visibility remains controlled by ShowEvictionNotice and its timer.
    }

    public void SetTax(string headline, string detail, Color accent)
    {
        // Compatibility for existing tax-machine callers; tax is no longer part of the HUD.
    }

    public void ShowEvictionNotice(string title, string body, float seconds)
    {
        if (_evictionRoot == null)
            return;

        if (_evictionRoutine != null)
            StopCoroutine(_evictionRoutine);

        _evictionTitle.text = title ?? string.Empty;
        _evictionBody.text = body ?? string.Empty;
        _evictionRoot.SetActive(true);
        _evictionRoutine = StartCoroutine(HideEvictionAfter(Mathf.Max(0.5f, seconds)));
    }

    private IEnumerator HideEvictionAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (_evictionRoot != null)
            _evictionRoot.SetActive(false);
        _evictionRoutine = null;
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _font = Resources.Load<TMP_FontAsset>(FontPath);
        _white = CreateWhiteSprite();

        Image evictionPanel = CreateImage(transform, "EvictionNotice", new Color(0.04f, 0.01f, 0.02f, 0.92f));
        _evictionRoot = evictionPanel.gameObject;
        SetStretch(evictionPanel.rectTransform, 0f, 0f, 0f, 0f);

        _evictionTitle = CreateText(evictionPanel.rectTransform, "Title", 88f, new Color(1f, 0.16f, 0.24f, 1f), TextAlignmentOptions.Center);
        _evictionTitle.fontStyle = FontStyles.Bold;
        RectTransform titleRect = _evictionTitle.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 0.5f);
        titleRect.anchorMax = new Vector2(1f, 0.5f);
        titleRect.pivot = new Vector2(0.5f, 0f);
        titleRect.anchoredPosition = new Vector2(0f, 20f);
        titleRect.sizeDelta = new Vector2(0f, 110f);

        _evictionBody = CreateText(evictionPanel.rectTransform, "Body", 28f, new Color(0.92f, 0.9f, 0.9f, 1f), TextAlignmentOptions.Top);
        RectTransform bodyRect = _evictionBody.rectTransform;
        bodyRect.anchorMin = new Vector2(0.15f, 0.5f);
        bodyRect.anchorMax = new Vector2(0.85f, 0.5f);
        bodyRect.pivot = new Vector2(0.5f, 1f);
        bodyRect.anchoredPosition = new Vector2(0f, -8f);
        bodyRect.sizeDelta = new Vector2(0f, 160f);
        _evictionRoot.SetActive(false);
    }

    private static void SetStretch(RectTransform rect, float left, float top, float right, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private Image CreateImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = _white;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private TextMeshProUGUI CreateText(Transform parent, string name, float fontSize, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        if (_font != null)
            text.font = _font;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    private static Sprite CreateWhiteSprite()
    {
        Texture2D texture = Texture2D.whiteTexture;
        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
    }
}
