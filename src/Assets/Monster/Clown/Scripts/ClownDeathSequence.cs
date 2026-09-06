using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ClownDeathSequence : MonoBehaviour, IMonsterDeathSequence
{
    [Header("Face")]
    [SerializeField] private Sprite faceSprite;
    [SerializeField] private float moveTime = 0.22f;
    [SerializeField] private float stayTime = 0.95f;
    [SerializeField] private float startOffset = 760f;
    [SerializeField] private float startScale = 1.1f;
    [SerializeField] private float pulseScale = 0.08f;

    [Header("Overlay")]
    [SerializeField] private bool useOverlay = true;
    [SerializeField] private Sprite overlaySprite;
    [SerializeField] private Color overlayColor = new(1f, 1f, 1f, 1f);
    [SerializeField] private float overlayMaxAlpha = 0.82f;
    [SerializeField] private float overlayMinAlpha = 0.2f;
    [SerializeField] private float overlayPulseFrequency = 8f;

    public IEnumerator PlayDeathSequence(PlayerDeath player)
    {
        CanvasGroup canvas = player.JumpscareCanvas;
        RectTransform faceRT = player.FaceImageRT;
        Image faceImage = player.FaceImage;
        Image overlay = player.OverlayImage;

        if (canvas != null)
        {
            canvas.gameObject.SetActive(true);
            canvas.alpha = 1f;
        }

        if (overlay != null)
        {
            overlay.gameObject.SetActive(useOverlay);
            if (overlaySprite != null)
                overlay.sprite = overlaySprite;

            Color overlayStart = overlayColor;
            overlayStart.a = 0f;
            overlay.color = overlayStart;
            overlay.rectTransform.SetAsFirstSibling();
        }

        if (faceRT != null)
        {
            faceRT.gameObject.SetActive(true);
            faceRT.SetAsLastSibling();
            faceRT.anchoredPosition = new Vector2(0f, startOffset);
            faceRT.localScale = Vector3.one * startScale;
        }

        if (faceImage != null)
        {
            if (faceSprite != null)
                faceImage.sprite = faceSprite;

            faceImage.enabled = true;
            faceImage.color = Color.white;
        }

        float moveElapsed = 0f;
        while (moveElapsed < moveTime)
        {
            moveElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(moveElapsed / Mathf.Max(0.0001f, moveTime));
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            if (faceRT != null)
            {
                faceRT.anchoredPosition = Vector2.Lerp(new Vector2(0f, startOffset), Vector2.zero, eased);
                faceRT.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, eased);
            }

            if (overlay != null && useOverlay)
            {
                Color c = overlayColor;
                c.a = Mathf.Lerp(0f, overlayMaxAlpha, eased);
                overlay.color = c;
            }

            yield return null;
        }

        float stayElapsed = 0f;
        while (stayElapsed < stayTime)
        {
            stayElapsed += Time.deltaTime;
            float pulse = Mathf.Sin(stayElapsed * overlayPulseFrequency) * 0.5f + 0.5f;

            if (overlay != null && useOverlay)
            {
                Color c = overlayColor;
                c.a = Mathf.Lerp(overlayMinAlpha, overlayMaxAlpha, pulse);
                overlay.color = c;
            }

            if (faceRT != null)
            {
                float scalePulse = 1f + pulseScale * pulse;
                faceRT.anchoredPosition = Vector2.zero;
                faceRT.localScale = Vector3.one * scalePulse;
            }

            yield return null;
        }

        player.CompleteDeathSequence();
    }
}
