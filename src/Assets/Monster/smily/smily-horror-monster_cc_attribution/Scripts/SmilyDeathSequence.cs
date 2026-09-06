using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class SmilyDeathSequence : MonoBehaviour, IMonsterDeathSequence
{
    [Header("Face")]
    [SerializeField] private Sprite faceSprite;
    [SerializeField] private float moveTime = 0.25f;
    [SerializeField] private float stayTime = 0.7f;
    [SerializeField] private float startOffset = 800f;
    [SerializeField] private float startScale = 1.2f;

    [Header("Shake")]
    [SerializeField] private float shakeAmount = 20f;
    [SerializeField] private float shakeSpeed = 40f;

    [Header("Overlay Default")]
    [SerializeField] private bool useOverlay = true;
    [SerializeField] private Color overlayColor = new Color(1f, 0f, 0f, 1f);
    [SerializeField] private float overlayMaxAlpha = 0.9f;
    [SerializeField] private Sprite overlaySprite;

    [Header("Flash")]
    [SerializeField] private bool useRandomColorFlash = true;
    [SerializeField] private float colorFlashInterval = 0.05f;
    [SerializeField] private float overlayMinAlpha = 0.3f;

    public IEnumerator PlayDeathSequence(PlayerDeath player)
    {
        CanvasGroup canvas = player.JumpscareCanvas;
        RectTransform faceRT = player.FaceImageRT;
        Image faceImg = player.FaceImage;
        Image overlay = player.OverlayImage;

        if (canvas != null)
        {
            canvas.gameObject.SetActive(true);
            canvas.alpha = 1f;
        }

        if (useOverlay && overlay != null)
        {
            overlay.gameObject.SetActive(true);

            if (overlaySprite != null)
                overlay.sprite = overlaySprite;

            Color overlayStart = overlayColor;
            overlayStart.a = 0f;
            overlay.color = overlayStart;
            overlay.rectTransform.SetAsFirstSibling();
        }

        if (faceRT != null && faceImg != null)
        {
            faceRT.gameObject.SetActive(true);
            faceRT.SetAsLastSibling();

            if (faceSprite != null)
                faceImg.sprite = faceSprite;

            faceImg.enabled = true;
        }

        Vector2 center = Vector2.zero;
        Vector2 startPos = new Vector2(0f, startOffset);

        if (faceRT != null)
        {
            faceRT.anchoredPosition = startPos;
            faceRT.localScale = Vector3.one * startScale;
        }

        float moveElapsed = 0f;
        while (moveElapsed < moveTime)
        {
            moveElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(moveElapsed / Mathf.Max(0.0001f, moveTime));
            float eased = t * t;

            if (faceRT != null)
                faceRT.anchoredPosition = Vector2.Lerp(startPos, center, eased);

            if (useOverlay && overlay != null)
            {
                Color c = overlayColor;
                c.a = Mathf.Lerp(0f, overlayMaxAlpha, eased);
                overlay.color = c;
            }

            yield return null;
        }

        if (faceRT != null)
            faceRT.anchoredPosition = center;

        float elapsed = 0f;
        float nextFlash = 0f;
        Vector2 basePos = center;

        while (elapsed < stayTime)
        {
            elapsed += Time.deltaTime;

            float shake = Mathf.Sin(elapsed * shakeSpeed);
            float shakeX = shake * shakeAmount;
            if (faceRT != null)
                faceRT.anchoredPosition = basePos + new Vector2(shakeX, 0f);

            if (useOverlay && overlay != null)
            {
                float alphaLerp = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, stayTime));
                float targetAlpha = Mathf.Lerp(overlayMinAlpha, overlayMaxAlpha, alphaLerp);

                if (useRandomColorFlash)
                {
                    if (elapsed >= nextFlash)
                    {
                        nextFlash += colorFlashInterval;

                        Color randomCol = Random.ColorHSV(
                            0f, 1f,
                            0.8f, 1f,
                            0.4f, 1f);
                        randomCol.a = targetAlpha;
                        overlay.color = randomCol;
                    }
                }
                else
                {
                    Color c = overlayColor;
                    c.a = targetAlpha;
                    overlay.color = c;
                }
            }

            yield return null;
        }

        if (faceRT != null)
            faceRT.anchoredPosition = center;

        player.CompleteDeathSequence();
    }
}
