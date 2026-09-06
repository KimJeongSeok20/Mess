using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 탄약 HUD의 잘린 모서리 패널을 스프라이트 없이 그린다.
/// 배경 그라데이션과 얇은 중성 회색 테두리를 한 번의 UI 메시로 구성한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class AmmoHudPanelGraphic : MaskableGraphic
{
    [SerializeField, Min(0f)] private float cornerCut = 11f;
    [SerializeField, Min(0f)] private float borderWidth = 1.35f;
    [SerializeField] private Color borderColor = new Color32(73, 78, 82, 220);
    [SerializeField] private Color topColor = new Color32(22, 25, 28, 242);
    [SerializeField] private Color bottomColor = new Color32(8, 10, 12, 246);

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();

        Rect outerRect = GetPixelAdjustedRect();
        if (outerRect.width <= 0f || outerRect.height <= 0f)
            return;

        float safeBorder = Mathf.Clamp(borderWidth, 0f, Mathf.Min(outerRect.width, outerRect.height) * 0.25f);
        Rect innerRect = new Rect(
            outerRect.xMin + safeBorder,
            outerRect.yMin + safeBorder,
            Mathf.Max(0f, outerRect.width - safeBorder * 2f),
            Mathf.Max(0f, outerRect.height - safeBorder * 2f));

        Vector2[] outer = BuildCorners(outerRect, cornerCut);
        Vector2[] inner = BuildCorners(innerRect, Mathf.Max(0f, cornerCut - safeBorder));

        for (int i = 0; i < outer.Length; i++)
            AddVertex(vertexHelper, outer[i], borderColor);

        for (int i = 0; i < inner.Length; i++)
        {
            float vertical = Mathf.InverseLerp(innerRect.yMin, innerRect.yMax, inner[i].y);
            AddVertex(vertexHelper, inner[i], Color.Lerp(bottomColor, topColor, vertical));
        }

        for (int i = 0; i < 8; i++)
        {
            int next = (i + 1) % 8;
            vertexHelper.AddTriangle(i, next, 8 + next);
            vertexHelper.AddTriangle(i, 8 + next, 8 + i);
        }

        for (int i = 1; i < 7; i++)
            vertexHelper.AddTriangle(8, 8 + i, 8 + i + 1);
    }

    private static Vector2[] BuildCorners(Rect rect, float requestedCut)
    {
        float cut = Mathf.Clamp(requestedCut, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
        return new[]
        {
            new Vector2(rect.xMin + cut, rect.yMin),
            new Vector2(rect.xMax - cut, rect.yMin),
            new Vector2(rect.xMax, rect.yMin + cut),
            new Vector2(rect.xMax, rect.yMax - cut),
            new Vector2(rect.xMax - cut, rect.yMax),
            new Vector2(rect.xMin + cut, rect.yMax),
            new Vector2(rect.xMin, rect.yMax - cut),
            new Vector2(rect.xMin, rect.yMin + cut)
        };
    }

    private static void AddVertex(VertexHelper vertexHelper, Vector2 position, Color color)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = color;
        vertexHelper.AddVert(vertex);
    }
}
