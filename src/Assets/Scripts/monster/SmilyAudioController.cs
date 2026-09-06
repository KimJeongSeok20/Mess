using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
[AddComponentMenu("Audio/Monster SFX Controller")]
public sealed class SmilyAudioController : MonoBehaviour
{
    public enum MonsterAudioProfile
    {
        Smily,
        Clown,
        Custom
    }

    [Header("Profile")]
    [SerializeField] private MonsterAudioProfile profile = MonsterAudioProfile.Smily;

    [Header("Source")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioSource loopSource;
    [SerializeField] private AudioSource oneShotSource;
    [SerializeField, Range(0f, 1f)] private float volume = 1f;
    [SerializeField, Range(0f, 1f)] private float loopVolume = 0.75f;
    [SerializeField] private float minDistance = 1f;
    [SerializeField] private float maxDistance = 18f;

    [Header("Event Volumes")]
    [SerializeField, Range(0f, 1f)] private float idleLoopVolume = 0.25f;
    [SerializeField, Range(0f, 1f)] private float patrolLoopVolume = 0.3f;
    [SerializeField, Range(0f, 1f)] private float detectVolume = 0.65f;
    [SerializeField, Range(0f, 1f)] private float warningSmileVolume = 0.75f;
    [SerializeField, Range(0f, 1f)] private float teleportWindupVolume = 0.75f;
    [SerializeField, Range(0f, 1f)] private float teleportVolume = 0.85f;
    [SerializeField, Range(0f, 1f)] private float attackWindupVolume = 0.9f;
    [SerializeField, Range(0f, 1f)] private float attackCommitVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float jumpLandVolume = 0.7f;
    [SerializeField, Range(0f, 1f)] private float fleeStartVolume = 0.65f;
    [SerializeField, Range(0f, 1f)] private float doorOpenVolume = 0.55f;
    [SerializeField, Range(0f, 1f)] private float deathVolume = 0.85f;
    [SerializeField, Range(0f, 1f)] private float goreExplosionVolume = 1f;

    [Header("Movement Loops")]
    [SerializeField] private AudioClip[] idleLoopClips;
    [SerializeField] private AudioClip[] patrolLoopClips;

    [Header("Awareness")]
    [SerializeField] private AudioClip[] detectClips;
    [SerializeField] private AudioClip[] warningSmileClips;
    [SerializeField] private AudioClip[] teleportWindupClips;
    [SerializeField] private AudioClip[] teleportClips;

    [Header("Attack")]
    [SerializeField] private AudioClip[] attackWindupClips;
    [SerializeField] private AudioClip[] attackCommitClips;
    [SerializeField] private AudioClip[] jumpAttackClips;
    [SerializeField] private AudioClip[] jumpLandClips;
    [SerializeField] private AudioClip[] fleeStartClips;
    [SerializeField] private AudioClip[] doorOpenClips;

    [Header("Death")]
    [SerializeField] private AudioClip[] deathClips;
    [SerializeField] private AudioClip[] goreExplosionClips;

    public string LastEventName { get; private set; }
    public MonsterAudioProfile Profile => profile;

    private bool _reportedMissingAudioSource;
    private string _activeLoopName = string.Empty;

    private void Awake()
    {
        EnsureAudioSources();
    }

    private void OnValidate()
    {
        if (volume < 0f)
            volume = 0f;

        if (loopVolume < 0f)
            loopVolume = 0f;

        idleLoopVolume = Mathf.Clamp01(idleLoopVolume);
        patrolLoopVolume = Mathf.Clamp01(patrolLoopVolume);
        detectVolume = Mathf.Clamp01(detectVolume);
        warningSmileVolume = Mathf.Clamp01(warningSmileVolume);
        teleportWindupVolume = Mathf.Clamp01(teleportWindupVolume);
        teleportVolume = Mathf.Clamp01(teleportVolume);
        attackWindupVolume = Mathf.Clamp01(attackWindupVolume);
        attackCommitVolume = Mathf.Clamp01(attackCommitVolume);
        jumpLandVolume = Mathf.Clamp01(jumpLandVolume);
        fleeStartVolume = Mathf.Clamp01(fleeStartVolume);
        doorOpenVolume = Mathf.Clamp01(doorOpenVolume);
        deathVolume = Mathf.Clamp01(deathVolume);
        goreExplosionVolume = Mathf.Clamp01(goreExplosionVolume);

        if (minDistance < 0.01f)
            minDistance = 0.01f;

        if (maxDistance < minDistance)
            maxDistance = minDistance;
    }

    public void PlayIdleLoop()
    {
        PlayLoop(idleLoopClips, "IdleLoop", idleLoopVolume);
    }

    public void PlayPatrolLoop()
    {
        PlayLoop(patrolLoopClips, "PatrolLoop", patrolLoopVolume);
    }

    public void StopMovementLoop(string eventName = "StopMovementLoop")
    {
        LastEventName = eventName;
        EnsureAudioSources();
        if (loopSource == null)
            return;

        if (loopSource.isPlaying && loopSource.loop)
            loopSource.Stop();

        loopSource.loop = false;
        loopSource.clip = null;
        _activeLoopName = string.Empty;
    }

    public void PlayDetect()
    {
        PlayRandom(detectClips, "Detect", detectVolume);
    }

    public void PlayWarningSmile()
    {
        PlayRandom(warningSmileClips, "WarningSmile", warningSmileVolume);
    }

    public void PlayTeleportWindup()
    {
        PlayRandom(teleportWindupClips, "TeleportWindup", teleportWindupVolume);
    }

    public void PlayTeleport()
    {
        PlayRandom(teleportClips, "Teleport", teleportVolume);
    }

    public void PlayAttackWindup()
    {
        PlayRandom(attackWindupClips, "AttackWindup", attackWindupVolume);
    }

    public void PlayAttackCommit()
    {
        PlayRandomWithFallback(attackCommitClips, jumpAttackClips, "AttackCommit", attackCommitVolume);
    }

    public void PlayJumpAttack()
    {
        PlayAttackCommit();
    }

    public void PlayJumpLand()
    {
        PlayRandom(jumpLandClips, "JumpLand", jumpLandVolume);
    }

    public void PlayFleeStart()
    {
        PlayRandom(fleeStartClips, "FleeStart", fleeStartVolume);
    }

    public void PlayDoorOpen()
    {
        PlayRandom(doorOpenClips, "DoorOpen", doorOpenVolume);
    }

    public void PlayDeath()
    {
        PlayRandom(deathClips, "Death", deathVolume);
    }

    public void PlayGoreExplosion()
    {
        PlayRandom(goreExplosionClips, "GoreExplosion", goreExplosionVolume);
    }

    private void PlayLoop(AudioClip[] clips, string eventName, float eventVolume)
    {
        LastEventName = eventName;
        EnsureAudioSources();
        if (loopSource == null)
            return;

        if (clips == null || clips.Length == 0)
        {
            StopMovementLoop(eventName);
            return;
        }

        if (_activeLoopName == eventName && loopSource.isPlaying && loopSource.loop)
            return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null)
        {
            StopMovementLoop(eventName);
            return;
        }

        _activeLoopName = eventName;
        loopSource.clip = clip;
        loopSource.loop = true;
        loopSource.volume = Mathf.Clamp01(volume * loopVolume * eventVolume);
        loopSource.Play();
    }

    private void PlayRandom(AudioClip[] clips, string eventName, float eventVolume)
    {
        LastEventName = eventName;

        if (clips == null || clips.Length == 0)
            return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null)
            return;

        EnsureAudioSources();
        if (oneShotSource == null)
            return;

        oneShotSource.PlayOneShot(clip, Mathf.Clamp01(volume * eventVolume));
    }

    private void PlayRandomWithFallback(AudioClip[] clips, AudioClip[] fallbackClips, string eventName, float eventVolume)
    {
        if (clips != null && clips.Length > 0)
        {
            PlayRandom(clips, eventName, eventVolume);
            return;
        }

        PlayRandom(fallbackClips, eventName, eventVolume);
    }

    private void EnsureAudioSources()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (oneShotSource == null)
            oneShotSource = audioSource;

        if (loopSource == null)
            loopSource = audioSource;

        if (oneShotSource == null && loopSource != null)
            oneShotSource = loopSource;

        if (audioSource == null && oneShotSource != null)
            audioSource = oneShotSource;

        if (audioSource == null)
        {
            ReportMissingAudioSource();
            return;
        }

        ConfigureSource(audioSource);
        ConfigureSource(oneShotSource);
        ConfigureSource(loopSource);
    }

    private void ConfigureSource(AudioSource source)
    {
        if (source == null)
            return;

        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.minDistance = Mathf.Max(0.01f, minDistance);
        source.maxDistance = Mathf.Max(source.minDistance, maxDistance);
        source.rolloffMode = AudioRolloffMode.Logarithmic;
    }

    private void ReportMissingAudioSource()
    {
        if (_reportedMissingAudioSource)
            return;

        _reportedMissingAudioSource = true;
        Debug.LogWarning("[MonsterSFX] Missing AudioSource. Fix the monster prefab/setup; runtime auto-attach is disabled.", this);
    }
}
