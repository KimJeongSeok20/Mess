using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class SmilyPortalEffect : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private LineRenderer outerRing;
    [SerializeField] private LineRenderer innerSpiral;
    [SerializeField] private LineRenderer jaggedEdge;
    [SerializeField] private ParticleSystem mistParticles;
    [SerializeField] private ParticleSystem sparkParticles;
    [SerializeField] private Light portalLight;

    [Header("Timing")]
    [SerializeField, Min(0.05f)] private float lifetime = 1.45f;
    [SerializeField, Min(0.01f)] private float openSeconds = 0.16f;
    [SerializeField, Min(0.01f)] private float closeSeconds = 0.28f;
    [SerializeField] private bool destroyOnComplete = true;

    [Header("Editor Preview")]
    [SerializeField] private bool previewInEditMode = true;
    [SerializeField, Range(0f, 1f)] private float editModeOpenAmount = 1f;
    [SerializeField] private bool animateInEditMode;
    [SerializeField, Min(0.05f)] private float editModeAnimationSeconds = 1.45f;

    [Header("Shape")]
    [SerializeField, Min(0.1f)] private float width = 1.15f;
    [SerializeField, Min(0.1f)] private float height = 2.25f;
    [SerializeField] private float centerHeight = 1.08f;
    [SerializeField, Range(0f, 0.25f)] private float edgeNoise = 0.08f;
    [SerializeField, Min(1f)] private float spiralTurns = 2.25f;
    [SerializeField] private float swirlSpeed = 4.5f;

    [Header("Color")]
    [SerializeField] private Color outerColor = new Color(0f, 0f, 0f, 0.96f);
    [SerializeField] private Color spiralColor = new Color(0f, 0f, 0f, 0.82f);
    [SerializeField] private Color edgeColor = new Color(0f, 0f, 0f, 0.95f);
    [SerializeField, Min(0f)] private float lightIntensity;

    private const int RingSegments = 96;
    private const int SpiralSegments = 72;

    private float _startedAt;

    private void Awake()
    {
        if (visualRoot == null)
            visualRoot = transform;
    }

    private void OnEnable()
    {
        _startedAt = GetTime();
        if (Application.isPlaying)
            PlayParticles();

        UpdatePortalForCurrentMode(0f);
    }

    private void OnValidate()
    {
        lifetime = Mathf.Max(0.05f, lifetime);
        openSeconds = Mathf.Max(0.01f, openSeconds);
        closeSeconds = Mathf.Max(0.01f, closeSeconds);
        width = Mathf.Max(0.1f, width);
        height = Mathf.Max(0.1f, height);
        spiralTurns = Mathf.Max(1f, spiralTurns);
        editModeAnimationSeconds = Mathf.Max(0.05f, editModeAnimationSeconds);

        if (!Application.isPlaying && isActiveAndEnabled)
            UpdatePortalForCurrentMode(0f);
    }

    private void Update()
    {
        float age = GetTime() - _startedAt;

        if (!Application.isPlaying)
        {
            UpdatePortalForCurrentMode(age);
            return;
        }

        UpdatePortal(age);

        if (age >= lifetime)
        {
            StopParticles();
            if (destroyOnComplete)
                Destroy(gameObject);
            else
                enabled = false;
        }
    }

    private void UpdatePortalForCurrentMode(float age)
    {
        if (Application.isPlaying)
        {
            UpdatePortal(age);
            return;
        }

        if (!previewInEditMode)
        {
            UpdatePortal(0f, 0f);
            return;
        }

        if (animateInEditMode)
        {
            float previewAge = Mathf.Repeat(age, Mathf.Max(0.05f, editModeAnimationSeconds));
            UpdatePortal(previewAge);
            PreviewParticles(previewAge);
            return;
        }

        UpdatePortal(openSeconds + 0.25f, editModeOpenAmount);
        PreviewParticles(0.25f);
    }

    private void UpdatePortal(float age)
    {
        UpdatePortal(age, ResolveOpenAmount(age));
    }

    private void UpdatePortal(float age, float open)
    {
        float easedOpen = Smooth(Mathf.Clamp01(open));
        float flicker = 0.85f + Mathf.Sin((age + transform.position.sqrMagnitude) * 41f) * 0.08f
            + Mathf.Sin(age * 17f) * 0.05f;

        if (visualRoot != null)
            visualRoot.localScale = Vector3.one * Mathf.Lerp(0.12f, 1f, easedOpen);

        UpdateRing(outerRing, age, easedOpen, false);
        UpdateRing(jaggedEdge, age, easedOpen, true);
        UpdateSpiral(innerSpiral, age, easedOpen);

        SetLineColor(outerRing, outerColor, easedOpen * flicker);
        SetLineColor(innerSpiral, spiralColor, easedOpen * flicker);
        SetLineColor(jaggedEdge, edgeColor, easedOpen);

        if (portalLight != null)
        {
            portalLight.color = outerColor;
            portalLight.intensity = lightIntensity * easedOpen * flicker;
        }
    }

    private float ResolveOpenAmount(float age)
    {
        float open = Mathf.Clamp01(age / openSeconds);
        float closeStart = Mathf.Max(openSeconds, lifetime - closeSeconds);
        if (age >= closeStart)
            open *= 1f - Mathf.Clamp01((age - closeStart) / closeSeconds);

        return open;
    }

    private static float Smooth(float value)
    {
        return value * value * (3f - 2f * value);
    }

    private void UpdateRing(LineRenderer line, float age, float open, bool jagged)
    {
        if (line == null)
            return;

        line.positionCount = RingSegments;
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;
        float time = age * swirlSpeed;

        for (int i = 0; i < RingSegments; i++)
        {
            float t = i / (float)RingSegments;
            float angle = t * Mathf.PI * 2f;
            float wobble = jagged
                ? 1f + Mathf.Sin(angle * 7f + time * 1.4f) * edgeNoise + Mathf.Sin(angle * 13f - time) * edgeNoise * 0.55f
                : 1f + Mathf.Sin(angle * 5f + time) * edgeNoise * 0.25f;
            float x = Mathf.Cos(angle) * halfWidth * wobble;
            float y = centerHeight + Mathf.Sin(angle) * halfHeight * wobble;
            float z = jagged ? Mathf.Sin(angle * 3f + time) * 0.035f : 0f;
            line.SetPosition(i, new Vector3(x, y, z));
        }

        line.widthMultiplier = (jagged ? 0.028f : 0.075f) * open;
    }

    private void UpdateSpiral(LineRenderer line, float age, float open)
    {
        if (line == null)
            return;

        line.positionCount = SpiralSegments;
        float halfWidth = width * 0.36f;
        float halfHeight = height * 0.36f;
        float time = age * swirlSpeed;

        for (int i = 0; i < SpiralSegments; i++)
        {
            float t = i / (float)(SpiralSegments - 1);
            float radius = Mathf.Pow(t, 0.78f);
            float angle = t * spiralTurns * Mathf.PI * 2f - time;
            float x = Mathf.Cos(angle) * halfWidth * radius;
            float y = centerHeight + Mathf.Sin(angle) * halfHeight * radius;
            line.SetPosition(i, new Vector3(x, y, -0.025f));
        }

        line.widthMultiplier = 0.04f * open;
    }

    private static void SetLineColor(LineRenderer line, Color color, float alphaMultiplier)
    {
        if (line == null)
            return;

        color.a *= Mathf.Clamp01(alphaMultiplier);
        line.startColor = color;
        line.endColor = color;
    }

    private void PlayParticles()
    {
        if (mistParticles != null)
        {
            mistParticles.Clear(true);
            mistParticles.Play(true);
        }

        if (sparkParticles != null)
        {
            sparkParticles.Clear(true);
            sparkParticles.Play(true);
        }
    }

    private void StopParticles()
    {
        if (mistParticles != null)
            mistParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        if (sparkParticles != null)
            sparkParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void PreviewParticles(float age)
    {
        SimulateParticlePreview(mistParticles, age);
        SimulateParticlePreview(sparkParticles, age);
    }

    private static void SimulateParticlePreview(ParticleSystem particles, float age)
    {
        if (particles == null)
            return;

        particles.Simulate(Mathf.Max(0f, age), true, true, true);
    }

    private static float GetTime()
    {
        return Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
    }
}
