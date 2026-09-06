using UnityEngine;
using TMPro;
using System.Collections;
using System;

/// <summary>
/// ����ȭ�� ������Ʈ ǥ�� �ý���
/// - Singleton �������� ��𼭵� ���� ����
/// - CanvasGroup + TextMeshProUGUI�� ��� (Reflection ����)
/// - �ε巯�� Fade �ִϸ��̼�
/// </summary>
public class PromptPresenter : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private CanvasGroup promptGroup;
    [SerializeField] private TextMeshProUGUI promptText;

    [Header("Animation Settings")]
    [SerializeField] private float fadeDuration = 0.15f;  // Fade �ִϸ��̼� �ӵ�
    [SerializeField] private bool useAnimation = true;    // �ִϸ��̼� ��� ����

    // Singleton
    private static PromptPresenter _instance;
    public static PromptPresenter Instance => _instance;

    // ���� ���� ���� �ڷ�ƾ
    private Coroutine _fadeCoroutine;

    #region Initialization

    private void Awake()
    {
        // Singleton ����
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("[PromptPresenter] �̹� �ν��Ͻ��� �����մϴ�. �ߺ� ����!");
            Destroy(gameObject);
            return;
        }

        _instance = this;

        if (promptGroup == null)
        {
            promptGroup = GetComponent<CanvasGroup>();
            if (promptGroup == null)
                promptGroup = GetComponentInChildren<CanvasGroup>(true);
        }

        if (promptText == null)
        {
            promptText = GetComponent<TextMeshProUGUI>();
            if (promptText == null)
                promptText = GetComponentInChildren<TextMeshProUGUI>(true);
            if (promptText == null && promptGroup != null)
                promptText = ResolvePromptTextFromCanvasGroup(promptGroup.transform);
        }

        // �ʼ� ������Ʈ ����
        if (promptGroup == null)
        {
            Debug.LogError("[PromptPresenter] CanvasGroup�� �Ҵ���� �ʾҽ��ϴ�!");
        }

        if (promptText == null)
        {
            Debug.LogError("[PromptPresenter] TextMeshProUGUI�� �Ҵ���� �ʾҽ��ϴ�!");
        }

        // �ʱ� ���� ����
        if (promptGroup != null)
        {
            promptGroup.alpha = 0f;
            promptGroup.interactable = false;
            promptGroup.blocksRaycasts = false;
        }
    }

    private void OnDestroy()
    {
        // Singleton ����
        if (_instance == this)
        {
            _instance = null;
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// ������Ʈ ǥ��
    /// </summary>
    /// <param name="text">ǥ���� �ؽ�Ʈ</param>
    public void Show(string text)
    {
        if (promptText == null || promptGroup == null)
        {
            Debug.LogWarning("[PromptPresenter] UI ������Ʈ�� �����ϴ�!");
            return;
        }

        // �ؽ�Ʈ ����
        promptText.text = text;

        // ���� �ִϸ��̼� �ߴ�
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
        }

        // Fade In
        if (useAnimation && gameObject.activeInHierarchy)
        {
            _fadeCoroutine = StartCoroutine(FadeToAlpha(1f));
        }
        else
        {
            // ��� ǥ��
            SetPromptVisibility(true);
        }
    }

    /// <summary>
    /// ������Ʈ �����
    /// </summary>
    public void Hide()
    {
        if (promptGroup == null)
        {
            Debug.LogWarning("[PromptPresenter] CanvasGroup�� �����ϴ�!");
            return;
        }

        // ���� �ִϸ��̼� �ߴ�
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
        }

        // Fade Out
        if (useAnimation && gameObject.activeInHierarchy)
        {
            _fadeCoroutine = StartCoroutine(FadeToAlpha(0f));
        }
        else
        {
            // ��� ����
            SetPromptVisibility(false);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Ư�� ���İ����� �ε巴�� ��ȯ
    /// </summary>
    private IEnumerator FadeToAlpha(float targetAlpha)
    {
        float startAlpha = promptGroup.alpha;
        float elapsed = 0f;

        // ��ǥ ���ķ� �̵� ���̸� ��ȣ�ۿ� ����
        if (targetAlpha > 0f)
        {
            promptGroup.interactable = true;
            promptGroup.blocksRaycasts = true;
        }

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            promptGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }

        // ���� ���İ� ����
        promptGroup.alpha = targetAlpha;

        // ������ �������� ��ȣ�ۿ� ��Ȱ��ȭ
        if (targetAlpha == 0f)
        {
            promptGroup.interactable = false;
            promptGroup.blocksRaycasts = false;
        }

        _fadeCoroutine = null;
    }

    /// <summary>
    /// ��� ǥ��/���� ���� (�ִϸ��̼� ����)
    /// </summary>
    private void SetPromptVisibility(bool visible)
    {
        promptGroup.alpha = visible ? 1f : 0f;
        promptGroup.interactable = visible;
        promptGroup.blocksRaycasts = visible;
    }

    private static TextMeshProUGUI ResolvePromptTextFromCanvasGroup(Transform promptRoot)
    {
        if (promptRoot == null)
            return null;

        var directText = promptRoot.Find("PromptText")?.GetComponent<TextMeshProUGUI>();
        if (directText != null)
            return directText;

        foreach (var text in promptRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text == null)
                continue;

            if (string.Equals(text.name, "PromptText", StringComparison.OrdinalIgnoreCase))
                return text;
        }

        var allTexts = FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var text in allTexts)
        {
            if (text == null)
                continue;

            if (!string.Equals(text.name, "PromptText", StringComparison.OrdinalIgnoreCase))
                continue;

            return text;
        }

        return null;
    }

    #endregion

    #region Static Access Methods (Optional)

    /// <summary>
    /// Static �޼���� ������Ʈ ǥ�� (Singleton ����)
    /// </summary>
    public static void ShowPrompt(string text)
    {
        if (Instance != null)
        {
            Instance.Show(text);
        }
        else
        {
            Debug.LogWarning("[PromptPresenter] Instance�� �����ϴ�! Scene�� PromptPresenter�� �ִ��� Ȯ���ϼ���.");
        }
    }

    /// <summary>
    /// Static �޼���� ������Ʈ ����� (Singleton ����)
    /// </summary>
    public static void HidePrompt()
    {
        if (Instance != null)
        {
            Instance.Hide();
        }
    }

    #endregion

    #region Debug Helpers

    [ContextMenu("Test Show")]
    private void TestShow()
    {
        Show("[F] Test Prompt");
    }

    [ContextMenu("Test Hide")]
    private void TestHide()
    {
        Hide();
    }

    #endregion
}
