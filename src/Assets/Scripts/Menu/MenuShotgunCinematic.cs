using UnityEngine;

/// <summary>A lightweight layered illustration with a repeating slow-motion shotgun discharge.</summary>
[DisallowMultipleComponent]
public sealed class MenuShotgunCinematic : MonoBehaviour
{
    [SerializeField] private RectTransform backdrop;
    [SerializeField] private RectTransform shooter;
    [SerializeField] private ShotgunPelletGraphic discharge;
    [SerializeField] private float loopSeconds = 8f;
    private float _elapsed;
    private Vector2 _shooterOrigin;
    private Vector2 _backgroundOrigin;

    private void Awake()
    {
        _shooterOrigin = shooter.anchoredPosition;
        _backgroundOrigin = backdrop.anchoredPosition;
    }

    private void OnEnable() => _elapsed = 0f;

    private void Update()
    {
        _elapsed = (_elapsed + Time.unscaledDeltaTime) % loopSeconds;
        float shot = Mathf.Clamp01((_elapsed - 0.9f) / 5.3f);
        float recoil = _elapsed < 0.9f ? 0f : Mathf.Exp(-(_elapsed - 0.9f) * 1.25f)
            * Mathf.Clamp01((_elapsed - 0.9f) * 12f);
        float drift = Mathf.Sin(_elapsed / loopSeconds * Mathf.PI * 2f);
        backdrop.anchoredPosition = _backgroundOrigin + new Vector2(drift * -8f, drift * 3f);
        backdrop.localScale = Vector3.one * (1.025f + Mathf.Sin(shot * Mathf.PI) * 0.015f);
        shooter.anchoredPosition = _shooterOrigin + new Vector2(-recoil * 30f + drift * 5f, -recoil * 9f);
        shooter.localEulerAngles = new Vector3(0, 0, recoil * 1.8f);
        discharge.SetShot(shot, _elapsed - 0.9f, new Vector2(-recoil * 30f + drift * 5f, -recoil * 9f));
    }
}
