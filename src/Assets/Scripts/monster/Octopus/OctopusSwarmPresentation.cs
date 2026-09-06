using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class OctopusSwarmPresentation : MonoBehaviour
{
    [Header("Sources")]
    [SerializeField] private Transform loopAnchor;
    [SerializeField] private AudioSource loopSource;
    [SerializeField, Range(0f, 1f)] private float volume = 1f;
    [SerializeField, Range(0f, 1f)] private float loopVolume = 0.65f;
    [SerializeField] private float minDistance = 1f;
    [SerializeField] private float maxDistance = 22f;

    [Header("Swarm Loops")]
    [SerializeField] private AudioClip[] patrolLoopClips;
    [SerializeField] private AudioClip[] alertLoopClips;

    [Header("Swarm One Shots")]
    [SerializeField] private AudioClip[] alertClips;
    [SerializeField] private AudioClip[] investigateClips;
    [SerializeField] private AudioClip[] calmClips;
    [SerializeField] private AudioClip[] defeatedClips;

    [Header("Member One Shots")]
    [SerializeField] private AudioClip[] attackWindupClips;
    [SerializeField] private AudioClip[] lungeClips;
    [SerializeField] private AudioClip[] hitClips;
    [SerializeField] private AudioClip[] damagedClips;
    [SerializeField] private AudioClip[] deathClips;

    [Header("Swarm VFX")]
    [SerializeField] private GameObject alertPulsePrefab;
    [SerializeField] private float alertPulseLifetime = 2f;
    [SerializeField] private GameObject defeatedPrefab;
    [SerializeField] private float defeatedLifetime = 3f;

    [Header("Member VFX")]
    [SerializeField] private GameObject attackWindupPrefab;
    [SerializeField] private float attackWindupLifetime = 1.2f;
    [SerializeField] private GameObject lungePrefab;
    [SerializeField] private float lungeLifetime = 1f;
    [SerializeField] private GameObject hitPrefab;
    [SerializeField] private float hitLifetime = 1.2f;
    [SerializeField] private GameObject damagedPrefab;
    [SerializeField] private float damagedLifetime = 1f;
    [SerializeField, Min(0.1f)] private float damagedVfxScale = 1f;
    [SerializeField] private GameObject deathPrefab;
    [SerializeField] private float deathLifetime = 2.5f;
    [SerializeField, Min(0.1f)] private float deathVfxScale = 1f;
    [SerializeField] private GameObject deathPoolPrefab;
    [SerializeField, Min(0.1f)] private float deathPoolScale = 0.55f;
    [SerializeField] private float deathDecalLifetime = 20f;
    [SerializeField] private GameObject explosiveDeathPrefab;
    [SerializeField] private float explosiveDeathLifetime = 3f;
    [SerializeField] private GameObject explosiveFragmentPrefab;
    [SerializeField, Min(1)] private int explosiveFragmentCount = 3;
    [SerializeField] private float explosiveFragmentForce = 3.5f;
    [SerializeField] private float explosiveFragmentLifetime = 8f;

    public string LastEventName { get; private set; }

    private string _activeLoopName = string.Empty;

    private void Awake()
    {
        EnsureLoopSource(true);
    }

    private void OnValidate()
    {
        volume = Mathf.Clamp01(volume);
        loopVolume = Mathf.Clamp01(loopVolume);
        minDistance = Mathf.Max(0.01f, minDistance);
        maxDistance = Mathf.Max(minDistance, maxDistance);
        alertPulseLifetime = Mathf.Max(0.1f, alertPulseLifetime);
        defeatedLifetime = Mathf.Max(0.1f, defeatedLifetime);
        attackWindupLifetime = Mathf.Max(0.1f, attackWindupLifetime);
        lungeLifetime = Mathf.Max(0.1f, lungeLifetime);
        hitLifetime = Mathf.Max(0.1f, hitLifetime);
        damagedLifetime = Mathf.Max(0.1f, damagedLifetime);
        deathLifetime = Mathf.Max(0.1f, deathLifetime);
        deathDecalLifetime = Mathf.Max(0.1f, deathDecalLifetime);
        explosiveDeathLifetime = Mathf.Max(0.1f, explosiveDeathLifetime);
        explosiveFragmentCount = Mathf.Max(1, explosiveFragmentCount);
        explosiveFragmentForce = Mathf.Max(0f, explosiveFragmentForce);
        explosiveFragmentLifetime = Mathf.Max(0.1f, explosiveFragmentLifetime);
        EnsureLoopSource(false);
    }

    public void SetSwarmAnchor(Vector3 position)
    {
        EnsureLoopSource(true);

        if (loopAnchor != null)
        {
            loopAnchor.position = position;
            return;
        }

        if (loopSource != null && loopSource.transform != transform)
            loopSource.transform.position = position;
    }

    public void PlayIntentTransition(MonsterIntent previous, MonsterIntent current, Vector3 focusPoint)
    {
        LastEventName = $"Intent:{previous}->{current}";

        switch (current)
        {
            case MonsterIntent.Chase:
                PlayLoop(alertLoopClips, "AlertLoop");
                if (previous != MonsterIntent.Attack)
                {
                    PlayDetachedOneShot(alertClips, "Alert", focusPoint);
                    SpawnDetached(alertPulsePrefab, focusPoint, Quaternion.identity, alertPulseLifetime);
                }
                break;
            case MonsterIntent.Attack:
                PlayLoop(alertLoopClips, "AlertLoop");
                break;
            case MonsterIntent.Investigate:
                PlayLoop(alertLoopClips, "InvestigateLoop");
                PlayDetachedOneShot(investigateClips, "Investigate", focusPoint);
                break;
            case MonsterIntent.Patrol:
                PlayLoop(patrolLoopClips, "PatrolLoop");
                if (previous is MonsterIntent.Chase or MonsterIntent.Investigate)
                    PlayDetachedOneShot(calmClips, "Calm", focusPoint);
                break;
            case MonsterIntent.Dead:
                StopLoop("Dead");
                break;
            default:
                StopLoop(current.ToString());
                break;
        }
    }

    public void PlayMemberAttackWindup(OctopusSwarmMember member, Vector3 position, Quaternion rotation)
    {
        PlayMemberOneShot(member, attackWindupClips, "AttackWindup", position);
        SpawnDetached(attackWindupPrefab, position, rotation, attackWindupLifetime);
    }

    public void PlayMemberLunge(OctopusSwarmMember member, Vector3 position, Quaternion rotation)
    {
        PlayMemberOneShot(member, lungeClips, "Lunge", position);
        SpawnDetached(lungePrefab, position, rotation, lungeLifetime);
    }

    public void PlayMemberHit(OctopusSwarmMember member, Vector3 position, Quaternion rotation)
    {
        PlayMemberOneShot(member, hitClips, "Hit", position);
        SpawnDetached(hitPrefab, position, rotation, hitLifetime);
    }

    public void PlayMemberDamaged(OctopusSwarmMember member, Vector3 position, Quaternion rotation)
    {
        PlayMemberOneShot(member, damagedClips, "Damaged", position);
        BloodVfxVisual.Spawn(damagedPrefab, position, rotation, damagedVfxScale, damagedLifetime);
    }

    public void PlayMemberDeath(OctopusSwarmMember member, Vector3 position, Quaternion rotation)
    {
        // Members are disabled shortly after death; let the cue finish independently.
        PlayDetachedOneShot(deathClips, "MemberDeath", position);
        BloodVfxVisual.Spawn(deathPrefab, position, rotation, deathVfxScale, deathLifetime);
        SpawnDeathDecal(position);
    }

    public void PlayMemberExplosiveDeath(OctopusSwarmMember member, Vector3 position, Quaternion rotation)
    {
        PlayDetachedOneShot(deathClips, "MemberExplosiveDeath", position);
        BloodVfxVisual.Spawn(explosiveDeathPrefab, position, rotation, deathVfxScale, explosiveDeathLifetime);
        SpawnDeathDecal(position);
        SpawnExplosiveFragments(position);
    }

    private void SpawnDeathDecal(Vector3 position)
    {
        BloodPoolVisual.SpawnOnGround(
            position,
            transform,
            deathPoolScale,
            deathPoolPrefab,
            deathDecalLifetime);
    }

    private void SpawnExplosiveFragments(Vector3 position)
    {
        if (explosiveFragmentPrefab == null)
            return;

        for (int i = 0; i < explosiveFragmentCount; i++)
        {
            Vector3 offset = Random.insideUnitSphere * 0.18f;
            offset.y = Mathf.Abs(offset.y) + 0.08f;
            GameObject fragment = Instantiate(
                explosiveFragmentPrefab,
                position + offset,
                Random.rotation);

            if (fragment.TryGetComponent<Rigidbody>(out Rigidbody rigidbody))
            {
                Vector3 direction = (offset.normalized + Vector3.up * 0.65f).normalized;
                rigidbody.AddForce(direction * explosiveFragmentForce, ForceMode.Impulse);
                rigidbody.AddTorque(Random.insideUnitSphere * explosiveFragmentForce, ForceMode.Impulse);
            }

            Destroy(fragment, explosiveFragmentLifetime);
        }
    }

    public void PlaySwarmDefeated(Vector3 position)
    {
        StopLoop("SwarmDefeated");
        PlayDetachedOneShot(defeatedClips, "SwarmDefeated", position);
        SpawnDetached(defeatedPrefab, position, Quaternion.identity, defeatedLifetime);
    }

    private void PlayLoop(AudioClip[] clips, string eventName)
    {
        LastEventName = eventName;
        EnsureLoopSource(true);
        if (loopSource == null)
            return;

        if (clips == null || clips.Length == 0)
        {
            StopLoop(eventName);
            return;
        }

        if (_activeLoopName == eventName && loopSource.isPlaying && loopSource.loop)
            return;

        AudioClip clip = PickClip(clips);
        if (clip == null)
        {
            StopLoop(eventName);
            return;
        }

        _activeLoopName = eventName;
        loopSource.clip = clip;
        loopSource.loop = true;
        loopSource.volume = Mathf.Clamp01(volume * loopVolume);
        loopSource.Play();
    }

    private void StopLoop(string eventName)
    {
        LastEventName = eventName;
        EnsureLoopSource(true);
        if (loopSource == null)
            return;

        if (loopSource.isPlaying && loopSource.loop)
            loopSource.Stop();

        loopSource.loop = false;
        loopSource.clip = null;
        _activeLoopName = string.Empty;
    }

    private void PlayDetachedOneShot(AudioClip[] clips, string eventName, Vector3 position)
    {
        LastEventName = eventName;

        AudioClip clip = PickClip(clips);
        if (clip == null)
            return;

        PlayDetachedOneShotClip(clip, eventName, position);
    }

    private void PlayMemberOneShot(OctopusSwarmMember member, AudioClip[] clips, string eventName, Vector3 position)
    {
        LastEventName = eventName;

        AudioClip clip = PickClip(clips);
        if (clip == null)
            return;

        float eventVolume = Mathf.Clamp01(volume);
        if (member != null && member.PlayPresentationOneShot(clip, eventVolume, minDistance, maxDistance))
            return;

        PlayDetachedOneShotClip(clip, eventName, position);
    }

    private void PlayDetachedOneShotClip(AudioClip clip, string eventName, Vector3 position)
    {
        if (clip == null)
            return;

        GameObject audioObject = new($"OctopusSFX_{eventName}");
        audioObject.transform.position = position;
        AudioSource source = audioObject.AddComponent<AudioSource>();
        ConfigureSource(source);
        source.outputAudioMixerGroup = loopSource != null ? loopSource.outputAudioMixerGroup : null;
        source.volume = 1f;
        source.PlayOneShot(clip, Mathf.Clamp01(volume));
        Destroy(audioObject, Mathf.Max(0.1f, clip.length + 0.1f));
    }

    private void SpawnDetached(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime)
    {
        if (prefab == null)
            return;

        GameObject instance = Instantiate(prefab, position, rotation);
        Destroy(instance, Mathf.Max(0.1f, lifetime));
    }

    private AudioClip PickClip(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0)
            return null;

        return clips[Random.Range(0, clips.Length)];
    }

    private void EnsureLoopSource(bool allowCreate)
    {
        if (loopSource == null && loopAnchor != null)
            loopSource = loopAnchor.GetComponent<AudioSource>();

        if (loopSource == null)
            loopSource = GetComponent<AudioSource>();

        if (allowCreate && (loopSource == null || loopSource.transform == transform))
        {
            if (loopAnchor == null || loopAnchor == transform)
            {
                Transform existingAnchor = transform.Find("OctopusSwarmAudioAnchor");
                if (existingAnchor != null)
                {
                    loopAnchor = existingAnchor;
                }
                else
                {
                    GameObject anchorObject = new("OctopusSwarmAudioAnchor");
                    anchorObject.transform.SetParent(transform, false);
                    loopAnchor = anchorObject.transform;
                }
            }

            AudioSource anchorSource = loopAnchor.GetComponent<AudioSource>();
            if (anchorSource == null)
                anchorSource = loopAnchor.gameObject.AddComponent<AudioSource>();

            loopSource = anchorSource;
        }

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
}
