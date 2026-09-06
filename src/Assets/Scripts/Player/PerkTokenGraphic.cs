using UnityEngine;

[DisallowMultipleComponent]
public sealed class PerkTokenGraphic : UnityEngine.UI.MaskableGraphic
{
    private static readonly Vector2[] ChipOutline =
    {
        new(0.13f, 0f), new(0.87f, 0f), new(1f, 0.13f), new(1f, 0.87f),
        new(0.87f, 1f), new(0.13f, 1f), new(0f, 0.87f), new(0f, 0.13f)
    };

    private PlayerPerkKind _kind;
    private Rect _meshRect;
    private Color _symbolColor;

    public void Configure(PlayerPerkKind kind)
    {
        _kind = kind;
        raycastTarget = false;
        color = kind switch
        {
            PlayerPerkKind.ExtraAirJump => new Color32(156, 211, 218, 255),
            PlayerPerkKind.GroundSlam => new Color32(233, 168, 115, 255),
            PlayerPerkKind.Adrenaline => new Color32(235, 198, 103, 255),
            PlayerPerkKind.SecondChance => new Color32(194, 169, 221, 255),
            PlayerPerkKind.FreeForge => new Color32(225, 191, 126, 255),
            PlayerPerkKind.LastStand => new Color32(239, 133, 133, 255),
            _ => new Color32(152, 207, 182, 255)
        };
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        _meshRect = GetPixelAdjustedRect();
        _symbolColor = color;

        AddVertex(vertexHelper, new Vector2(0.5f, 0.5f), new Color32(11, 17, 21, 210));
        for (int i = 0; i < ChipOutline.Length; i++)
            AddVertex(vertexHelper, ChipOutline[i], new Color32(11, 17, 21, 210));
        for (int i = 0; i < ChipOutline.Length; i++)
            vertexHelper.AddTriangle(0, i + 1, (i + 1) % ChipOutline.Length + 1);

        Color border = _symbolColor;
        border.a *= 0.35f;
        for (int i = 0; i < ChipOutline.Length; i++)
            Line(vertexHelper, ChipOutline[i], ChipOutline[(i + 1) % ChipOutline.Length], border, 0.7f);

        switch (_kind)
        {
            case PlayerPerkKind.ExtraAirJump:
                UpArrow(vertexHelper, 0.34f, 0.27f, 0.74f);
                UpArrow(vertexHelper, 0.66f, 0.27f, 0.74f);
                break;
            case PlayerPerkKind.GroundSlam:
                Stroke(vertexHelper, 0.5f, 0.79f, 0.5f, 0.39f);
                Stroke(vertexHelper, 0.35f, 0.54f, 0.5f, 0.39f);
                Stroke(vertexHelper, 0.65f, 0.54f, 0.5f, 0.39f);
                Stroke(vertexHelper, 0.22f, 0.25f, 0.78f, 0.25f);
                Stroke(vertexHelper, 0.15f, 0.43f, 0.26f, 0.36f);
                Stroke(vertexHelper, 0.85f, 0.43f, 0.74f, 0.36f);
                break;
            case PlayerPerkKind.Adrenaline:
                Stroke(vertexHelper, 0.17f, 0.48f, 0.32f, 0.48f);
                Stroke(vertexHelper, 0.32f, 0.48f, 0.43f, 0.73f);
                Stroke(vertexHelper, 0.43f, 0.73f, 0.55f, 0.27f);
                Stroke(vertexHelper, 0.55f, 0.27f, 0.67f, 0.52f);
                Stroke(vertexHelper, 0.67f, 0.52f, 0.83f, 0.52f);
                break;
            case PlayerPerkKind.SafetyNet:
                Shield(vertexHelper);
                Stroke(vertexHelper, 0.35f, 0.51f, 0.46f, 0.40f);
                Stroke(vertexHelper, 0.46f, 0.40f, 0.65f, 0.61f);
                break;
            case PlayerPerkKind.SecondChance:
                Arc(vertexHelper, new Vector2(0.5f, 0.5f), 0.28f, 40f, 325f);
                Stroke(vertexHelper, 0.71f, 0.68f, 0.57f, 0.70f);
                Stroke(vertexHelper, 0.71f, 0.68f, 0.72f, 0.53f);
                break;
            case PlayerPerkKind.FreeForge:
                Stroke(vertexHelper, 0.29f, 0.25f, 0.71f, 0.25f);
                Stroke(vertexHelper, 0.39f, 0.25f, 0.43f, 0.43f);
                Stroke(vertexHelper, 0.61f, 0.25f, 0.57f, 0.43f);
                Stroke(vertexHelper, 0.22f, 0.51f, 0.78f, 0.51f);
                Stroke(vertexHelper, 0.22f, 0.51f, 0.35f, 0.42f);
                Stroke(vertexHelper, 0.35f, 0.42f, 0.66f, 0.42f);
                Stroke(vertexHelper, 0.53f, 0.62f, 0.66f, 0.75f);
                Stroke(vertexHelper, 0.58f, 0.77f, 0.71f, 0.64f);
                break;
            case PlayerPerkKind.LastStand:
                UpArrow(vertexHelper, 0.53f, 0.26f, 0.77f);
                Stroke(vertexHelper, 0.24f, 0.24f, 0.79f, 0.24f);
                Stroke(vertexHelper, 0.22f, 0.56f, 0.22f, 0.76f);
                Stroke(vertexHelper, 0.12f, 0.66f, 0.32f, 0.66f);
                break;
            case PlayerPerkKind.TeamInsurance:
                Arc(vertexHelper, new Vector2(0.36f, 0.66f), 0.08f, 0f, 360f);
                Arc(vertexHelper, new Vector2(0.65f, 0.66f), 0.08f, 0f, 360f);
                Stroke(vertexHelper, 0.21f, 0.42f, 0.29f, 0.51f);
                Stroke(vertexHelper, 0.29f, 0.51f, 0.42f, 0.51f);
                Stroke(vertexHelper, 0.58f, 0.51f, 0.72f, 0.51f);
                Stroke(vertexHelper, 0.72f, 0.51f, 0.80f, 0.42f);
                Stroke(vertexHelper, 0.22f, 0.34f, 0.5f, 0.20f);
                Stroke(vertexHelper, 0.5f, 0.20f, 0.78f, 0.34f);
                break;
        }
    }

    private void UpArrow(UnityEngine.UI.VertexHelper vertexHelper, float x, float bottom, float top)
    {
        Stroke(vertexHelper, x, bottom, x, top);
        Stroke(vertexHelper, x - 0.12f, top - 0.14f, x, top);
        Stroke(vertexHelper, x + 0.12f, top - 0.14f, x, top);
    }

    private void Shield(UnityEngine.UI.VertexHelper vertexHelper)
    {
        Stroke(vertexHelper, 0.24f, 0.77f, 0.76f, 0.77f);
        Stroke(vertexHelper, 0.76f, 0.77f, 0.72f, 0.43f);
        Stroke(vertexHelper, 0.72f, 0.43f, 0.5f, 0.22f);
        Stroke(vertexHelper, 0.5f, 0.22f, 0.28f, 0.43f);
        Stroke(vertexHelper, 0.28f, 0.43f, 0.24f, 0.77f);
    }

    private void Arc(UnityEngine.UI.VertexHelper vertexHelper, Vector2 center, float radius, float fromDegrees, float toDegrees)
    {
        const int segments = 18;
        float from = fromDegrees * Mathf.Deg2Rad;
        Vector2 previous = center + new Vector2(Mathf.Cos(from), Mathf.Sin(from)) * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = Mathf.Lerp(fromDegrees, toDegrees, i / (float)segments) * Mathf.Deg2Rad;
            Vector2 current = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            Line(vertexHelper, previous, current, _symbolColor, 1.3f);
            previous = current;
        }
    }

    private void Stroke(UnityEngine.UI.VertexHelper vertexHelper, float fromX, float fromY, float toX, float toY)
    {
        Line(vertexHelper, new Vector2(fromX, fromY), new Vector2(toX, toY), _symbolColor, 1.3f);
    }

    private void Line(UnityEngine.UI.VertexHelper vertexHelper, Vector2 from, Vector2 to, Color tint, float width)
    {
        Vector2 normal = new Vector2(-(to - from).y, (to - from).x).normalized * (width / 48f);
        int start = vertexHelper.currentVertCount;
        AddVertex(vertexHelper, from - normal, tint);
        AddVertex(vertexHelper, from + normal, tint);
        AddVertex(vertexHelper, to + normal, tint);
        AddVertex(vertexHelper, to - normal, tint);
        vertexHelper.AddTriangle(start, start + 1, start + 2);
        vertexHelper.AddTriangle(start, start + 2, start + 3);
    }

    private void AddVertex(UnityEngine.UI.VertexHelper vertexHelper, Vector2 normalized, Color tint)
    {
        UnityEngine.UIVertex vertex = UnityEngine.UIVertex.simpleVert;
        vertex.position = new Vector2(
            Mathf.LerpUnclamped(_meshRect.xMin, _meshRect.xMax, normalized.x),
            Mathf.LerpUnclamped(_meshRect.yMin, _meshRect.yMax, normalized.y));
        vertex.color = tint;
        vertex.uv0 = Vector2.zero;
        vertexHelper.AddVert(vertex);
    }
}
