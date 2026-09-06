using UnityEngine;

/// <summary>Personal feedback on the owning player, independent of spatial footsteps and gunfire.</summary>
[DisallowMultipleComponent]
public sealed class PlayerFeedbackAudio : MonoBehaviour
{
    [Header("Player References")]
    [SerializeField] private NetworkPlayer player;
    [SerializeField] private PlayerVitals vitals;
    [SerializeField] private AudioSource feedbackSource;

    [Header("Clips")]
    [SerializeField] private AudioClip[] hurtClips;
    [SerializeField] private AudioClip deathClip;
    [SerializeField] private AudioClip pickupClip;
    [SerializeField] private AudioClip inventoryOpenClip;
    [SerializeField] private AudioClip inventoryCloseClip;
    [SerializeField] private AudioClip dryFireClip;

    [Header("Levels")]
    [SerializeField, Range(0f, 1f)] private float hurtVolume = 0.55f;
    [SerializeField, Range(0f, 1f)] private float deathVolume = 0.65f;
    [SerializeField, Range(0f, 1f)] private float pickupVolume = 0.4f;
    [SerializeField, Range(0f, 1f)] private float inventoryVolume = 0.3f;
    [SerializeField, Range(0f, 1f)] private float dryFireVolume = 0.55f;

    private bool _ready;
    private bool _deathPlayed;
    private int _previousHealth;
    private int _previousMaxHealth;
    private float _nextHurtTime;
    private float _nextDryFireTime;

    private bool IsOwner => player != null && player.isSpawned && player.isOwner;

    private void OnEnable()
    {
        _ready = false;
        if (vitals == null) return;
        vitals.OnHealthChanged += HandleHealthChanged;
        vitals.OnDied += HandleDeath;
    }

    private void OnDisable()
    {
        if (vitals != null)
        {
            vitals.OnHealthChanged -= HandleHealthChanged;
            vitals.OnDied -= HandleDeath;
        }
        if (feedbackSource != null) feedbackSource.Stop();
        _ready = false;
    }

    private void LateUpdate()
    {
        if (!IsOwner)
        {
            if (_ready && feedbackSource != null) feedbackSource.Stop();
            _ready = false;
            return;
        }

        // Capture the initial replicated state after spawn callbacks; joining is not damage.
        if (_ready || vitals == null) return;
        _previousHealth = vitals.CurrentHealth;
        _previousMaxHealth = vitals.MaxHealth;
        _deathPlayed = vitals.IsDead;
        _ready = true;
    }

    private void HandleHealthChanged(int health, int maximum)
    {
        bool tookDamage = _ready && maximum == _previousMaxHealth && health < _previousHealth;
        _previousHealth = health;
        _previousMaxHealth = maximum;
        if (health > 0) _deathPlayed = false;

        if (!tookDamage || health <= 0 || !IsOwner || Time.unscaledTime < _nextHurtTime
            || hurtClips == null || hurtClips.Length == 0) return;

        _nextHurtTime = Time.unscaledTime + 0.35f;
        Play(hurtClips[Random.Range(0, hurtClips.Length)], hurtVolume);
    }

    private void HandleDeath()
    {
        if (!_ready || !IsOwner || _deathPlayed) return;
        _deathPlayed = true;
        if (feedbackSource != null) feedbackSource.Stop();
        Play(deathClip, deathVolume);
    }

    public void PlayPickup()
    {
        if (vitals != null && !vitals.IsDead) Play(pickupClip, pickupVolume);
    }

    public void PlayInventoryToggle(bool open)
    {
        if (vitals != null && !vitals.IsDead)
            Play(open ? inventoryOpenClip : inventoryCloseClip, inventoryVolume);
    }

    public void PlayDryFire()
    {
        if (vitals == null || vitals.IsDead || !IsOwner || Time.unscaledTime < _nextDryFireTime) return;
        _nextDryFireTime = Time.unscaledTime + 0.2f;
        Play(dryFireClip, dryFireVolume);
    }

    private void Play(AudioClip clip, float volume)
    {
        if (_ready && IsOwner && feedbackSource != null && clip != null)
            feedbackSource.PlayOneShot(clip, volume);
    }
}
