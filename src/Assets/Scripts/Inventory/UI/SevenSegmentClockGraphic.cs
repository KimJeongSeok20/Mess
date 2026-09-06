using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("UI/Seven Segment Clock")]
public class SevenSegmentClockGraphic : UnityEngine.UI.MaskableGraphic
{
    private const float DigitHeight = 1.8f;
    private const float StrokeWidth = 0.11f;
    private static readonly int[] DigitMasks = { 0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f };
    private static readonly Vector2[] SegmentStarts =
    {
        new(0.13f, 1.73f), new(0.94f, 1.63f), new(0.94f, 0.79f),
        new(0.13f, 0.07f), new(0.06f, 0.79f), new(0.06f, 1.63f), new(0.13f, 0.9f)
    };
    private static readonly Vector2[] SegmentEnds =
    {
        new(0.87f, 1.73f), new(0.94f, 1.01f), new(0.94f, 0.17f),
        new(0.87f, 0.07f), new(0.06f, 0.17f), new(0.06f, 1.01f), new(0.87f, 0.9f)
    };

    [SerializeField] private string displayText = "00 00:00";

    public string DisplayText => displayText;

    public void SetText(string value)
    {
        value ??= string.Empty;
        if (displayText == value)
            return;

        displayText = value;
        SetVerticesDirty();
    }

#if UNITY_EDITOR
    protected override void Reset()
    {
        base.Reset();
        raycastTarget = false;
    }
#endif

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        if (string.IsNullOrEmpty(displayText))
            return;

        float width = 0f;
        foreach (char character in displayText)
            width += Advance(character);
        width -= 0.18f;

        Rect rect = GetPixelAdjustedRect();
        float scale = Mathf.Min(rect.width / width, rect.height / DigitHeight);
        if (scale <= 0f)
            return;

        Vector2 origin = rect.center - new Vector2(width, DigitHeight) * (scale * 0.5f);
        foreach (char character in displayText)
        {
            DrawGlyph(mesh, char.ToUpperInvariant(character), origin, scale);
            origin.x += Advance(character) * scale;
        }
    }

    private static float Advance(char character) => character == ':' ? 0.42f : character == ' ' ? 0.52f : 1.18f;

    private void DrawGlyph(UnityEngine.UI.VertexHelper mesh, char character, Vector2 origin, float scale)
    {
        bool numeric = character >= '0' && character <= '9';
        int mask = numeric ? DigitMasks[character - '0'] : character switch
        {
            'A' => 0x77,
            'P' => 0x73,
            '-' => 0x40,
            _ => 0
        };

        for (int segment = 0; segment < 7; segment++)
        {
            bool lit = (mask & (1 << segment)) != 0;
            if (!lit && !numeric)
                continue;

            Color segmentColor = color;
            if (!lit)
                segmentColor.a *= 0.06f;
            AddStroke(mesh, SegmentStarts[segment], SegmentEnds[segment], origin, scale, segmentColor);
        }

        if (character == ':')
        {
            AddStroke(mesh, new Vector2(0.12f, 0.54f), new Vector2(0.12f, 0.69f), origin, scale, color);
            AddStroke(mesh, new Vector2(0.12f, 1.11f), new Vector2(0.12f, 1.26f), origin, scale, color);
        }
        else if (character == 'M')
        {
            AddStroke(mesh, new Vector2(0.06f, 0.07f), new Vector2(0.06f, 1.73f), origin, scale, color);
            AddStroke(mesh, new Vector2(0.06f, 1.73f), new Vector2(0.5f, 1.05f), origin, scale, color);
            AddStroke(mesh, new Vector2(0.5f, 1.05f), new Vector2(0.94f, 1.73f), origin, scale, color);
            AddStroke(mesh, new Vector2(0.94f, 1.73f), new Vector2(0.94f, 0.07f), origin, scale, color);
        }
    }

    private static void AddStroke(UnityEngine.UI.VertexHelper mesh, Vector2 start, Vector2 end,
        Vector2 origin, float scale, Color tint)
    {
        Vector2 bevel = (end - start).normalized * (StrokeWidth * 0.5f);
        Vector2 side = new(-bevel.y, bevel.x);
        int first = mesh.currentVertCount;
        mesh.AddVert(origin + start * scale, tint, Vector2.zero);
        mesh.AddVert(origin + (start + bevel + side) * scale, tint, Vector2.zero);
        mesh.AddVert(origin + (end - bevel + side) * scale, tint, Vector2.zero);
        mesh.AddVert(origin + end * scale, tint, Vector2.zero);
        mesh.AddVert(origin + (end - bevel - side) * scale, tint, Vector2.zero);
        mesh.AddVert(origin + (start + bevel - side) * scale, tint, Vector2.zero);
        for (int triangle = 1; triangle < 5; triangle++)
            mesh.AddTriangle(first, first + triangle, first + triangle + 1);
    }
}
