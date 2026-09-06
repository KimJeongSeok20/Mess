using TMPro;
using UnityEngine;

[CreateAssetMenu(fileName = "InventoryTheme", menuName = "Inventory/UI Theme")]
public class InventoryTheme : ScriptableObject
{
    [Header("Surface")]
    public Color dim;
    public Color surface0;
    public Color surface1;
    public Color surface2;
    public Color surface3;
    public Color surfaceLocked;

    [Header("Border")]
    public Color borderSubtle;
    public Color borderStrong;

    [Tooltip("잠금 슬롯 윤곽선. 패널과 거의 동화되도록 낮은 알파를 쓴다.")]
    public Color borderLocked;

    [Header("Text")]
    public Color textPrimary;
    public Color textSecondary;
    public Color textDisabled;

    [Header("Accent / Semantic")]
    public Color accent;
    public Color accentDim;
    public Color danger;
    public Color success;

    [Header("Rarity")]
    public Color rarityCommon;
    public Color rarityUncommon;
    public Color rarityRare;
    public Color rarityEpic;
    public Color rarityLegendary;

    [Header("Spacing")]
    public float spacing4;
    public float spacing8;
    public float spacing12;
    public float spacing16;
    public float spacing24;
    public float spacing32;

    [Header("Corner Radius")]
    public float slotCornerRadius;
    public float panelCornerRadius;
    public float buttonCornerRadius;

    [Header("Fonts")]
    [Tooltip("헤더/아이템 이름 등 표제용. 비어 있으면 bodyFont를 쓴다.")]
    public TMP_FontAsset titleFont;
    [Tooltip("본문·라벨·수치용 기본 서체.")]
    public TMP_FontAsset bodyFont;

    [Header("Type")]
    public float titleSize;
    public float labelSize;
    public float bodySize;
    public float captionSize;
    public float numericSize;
    public float titleLetterSpacing;
    public FontWeight titleWeight;
    public FontWeight labelWeight;
    public FontWeight bodyWeight;
    public FontWeight captionWeight;
    public FontWeight numericWeight;

    [Header("Motion (seconds)")]
    public float panelFade;
    public float panelScale;
    public float panelScaleFrom;
    public float panelScaleTo;
    public float slotState;
    public float slotStagger;
    public float slotStaggerMax;

    [Header("Sprites")]
    public Sprite panelSprite;
    [Tooltip("패널 가장자리 1px 윤곽. 배경과 패널의 경계를 분명히 한다.")]
    public Sprite panelOutlineSprite;
    public Sprite slotSprite;
    public Sprite slotOutlineSprite;
    public Sprite buttonSprite;
    public Sprite dividerSprite;
}
