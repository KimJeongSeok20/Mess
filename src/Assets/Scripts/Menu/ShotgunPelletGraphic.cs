using UnityEngine;

/// <summary>Actual moving pellet, casing and muzzle-flash geometry over the generated scene layers.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ShotgunPelletGraphic : UnityEngine.UI.MaskableGraphic
{
    [SerializeField] private Vector2 muzzle = new(0.58f, 0.49f);
    [SerializeField] private Vector2 target = new(0.84f, 0.64f);
    private float _progress;
    private float _time;
    private Vector2 _recoil;
    private static readonly Vector2[] Spread = {
        new(-0.06f, 0.085f), new(-0.025f, 0.045f), new(0.035f, 0.115f),
        new(0.065f, 0.065f), new(0.085f, 0.005f), new(0.035f, -0.045f),
        new(-0.015f, -0.085f), new(-0.065f, -0.025f), new(0.005f, 0.015f)
    };

    protected override void Awake() { base.Awake(); raycastTarget = false; }

    public void SetShot(float progress, float shotTime, Vector2 recoil)
    {
        _progress = progress; _time = shotTime; _recoil = recoil;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();
        if (_time < 0f || _time > 6.5f) return;
        Rect rect = rectTransform.rect;
        Vector2 start = At(muzzle, rect) + _recoil;
        float flare = Mathf.Clamp01(_time * 15f) * Mathf.Exp(-_time * 2.7f);
        Glow(vh, start, rect.width * 0.074f, new Color(1f, 0.46f, 0.10f, flare * 0.34f));
        Glow(vh, start, rect.width * 0.026f, new Color(1f, 0.83f, 0.38f, flare * 0.85f));
        for (int ray = 0; ray < 7; ray++)
        {
            float angle = ray * (Mathf.PI * 2f / 7f) + 0.2f;
            Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
            Streak(vh, start, start + direction * rect.width * 0.041f * flare,
                rect.width * 0.003f, new Color(1f, 0.93f, 0.64f, flare));
        }
        float fade = Mathf.Clamp01((1f - _progress) * 7f);
        for (int i = 0; i < Spread.Length; i++)
        {
            Vector2 end = At(target + Spread[i], rect);
            float travel = Mathf.Clamp01(_progress * (0.94f + i * 0.014f));
            Vector2 point = Vector2.Lerp(start, end, travel);
            Vector2 direction = (end - start).normalized;
            float width = Mathf.Lerp(3.6f, 1.3f, travel) * rect.width / 1920f;
            float length = Mathf.Lerp(46f, 13f, travel) * rect.width / 1920f;
            Streak(vh, point - direction * length, point, width * 3f, new Color(1f, 0.58f, 0.18f, fade * 0.22f));
            Streak(vh, point - direction * length * 0.6f, point, width, new Color(1f, 0.90f, 0.57f, fade));
            Glow(vh, point, width * 2.2f, new Color(1f, 0.85f, 0.46f, fade));
        }
        // A single cartridge turns and falls while the pellets travel away from the muzzle.
        float casingT = Mathf.Clamp01(_time / 4f);
        Vector2 casing = start + new Vector2(-rect.width * (0.075f + casingT * 0.06f),
            rect.height * (casingT * 0.14f - casingT * casingT * 0.23f));
        Vector2 casingAxis = new Vector2(Mathf.Cos(casingT * 11f), Mathf.Sin(casingT * 11f)) * rect.width * 0.006f;
        Streak(vh, casing - casingAxis, casing + casingAxis, rect.width * 0.004f,
            new Color(0.77f, 0.25f, 0.10f, fade));
    }

    private static Vector2 At(Vector2 uv, Rect rect) => new(rect.xMin + uv.x * rect.width, rect.yMin + uv.y * rect.height);

    private static void Streak(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 axis = b - a;
        if (axis.sqrMagnitude < 0.001f) return;
        Vector2 normal = new Vector2(-axis.y, axis.x).normalized * width;
        int index = vh.currentVertCount;
        vh.AddVert(a - normal, color, Vector2.zero); vh.AddVert(a + normal, color, Vector2.zero);
        vh.AddVert(b + normal, color, Vector2.zero); vh.AddVert(b - normal, color, Vector2.zero);
        vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index, index + 2, index + 3);
    }

    private static void Glow(UnityEngine.UI.VertexHelper vh, Vector2 center, float radius, Color color)
    {
        int index = vh.currentVertCount;
        vh.AddVert(center, color, Vector2.zero);
        Color edge = new(color.r, color.g, color.b, 0f);
        const int segments = 16;
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, edge, Vector2.zero);
            if (i > 0) vh.AddTriangle(index, index + i, index + i + 1);
        }
    }
}
