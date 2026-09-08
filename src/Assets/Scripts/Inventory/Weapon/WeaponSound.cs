using UnityEngine;
using Demo.Scripts.Runtime.Item;
using PurrNet;

[DisallowMultipleComponent]
public class WeaponSound : MonoBehaviour
{
    private static string _lastReloadPlaybackDebug = "unset";

    [Header("Optional Override")]
    [SerializeField] private AudioSource audioSourceOverride;

    private Weapon _weapon;
    private AudioSource _audio;
    private int _localReloadCount;
    private int _remoteReloadCount;

    private void Awake()
    {
        _weapon = GetComponentInParent<Weapon>();

        if (_weapon == null)
            Debug.LogWarning("[WeaponSound] Parent���� Weapon�� ã�� ���߽��ϴ�.");

        EnsureAudioSourceBinding(logIfMissing: true);
    }

    private bool EnsureAudioSourceBinding(bool logIfMissing = false)
    {
        if (_audio != null) return true;

        var audioRouter = GetComponentInParent<PlayerAudioRouter>()
            ?? transform.root.GetComponentInChildren<PlayerAudioRouter>(true);

        _audio = audioSourceOverride
              ?? audioRouter?.WeaponAudioSource
              ?? GetComponentInParent<AudioSource>()
              ?? GetComponent<AudioSource>()
              ?? transform.root.GetComponentInChildren<AudioSource>(true);

        if (_audio != null)
        {
            string groupName = _audio.outputAudioMixerGroup != null ? _audio.outputAudioMixerGroup.name : "null";
#if UNITY_EDITOR
            Debug.Log($"[WeaponSound] Bound AudioSource={_audio.GetInstanceID()} group={groupName} on {name}", this);
#endif
            Apply3DSettingsIfPossible();
            return true;
        }

        if (logIfMissing)
            Debug.LogWarning("[WeaponSound] AudioSource�� ã�� ���߽��ϴ�. Player(����) �Ǵ� Weapon�� AudioSource�� �߰��ϼ���.", this);

        return false;
    }

    public void BindAudioSource(AudioSource audioSource, bool logIfMissing = true)
    {
        if (audioSource == null)
        {
            if (logIfMissing)
                Debug.LogWarning("[WeaponSound] BindAudioSource called with null AudioSource.", this);

            return;
        }

        _audio = audioSource;

        string groupName = _audio.outputAudioMixerGroup != null ? _audio.outputAudioMixerGroup.name : "null";
#if UNITY_EDITOR
        Debug.Log($"[WeaponSound] Injected AudioSource={_audio.GetInstanceID()} group={groupName} on {name}", this);
#endif
        Apply3DSettingsIfPossible();
    }

    private void Apply3DSettingsIfPossible()
    {
        if (_weapon == null || _weapon.WeaponData == null || _audio == null) return;

        var sfx = _weapon.WeaponData.sfx;
        _audio.dopplerLevel = 0f;

        if (!sfx.use3D)
        {
            _audio.spatialBlend = 0f; // 2D
            return;
        }

        _audio.spatialBlend = 1f; // 3D
        _audio.minDistance = Mathf.Max(0.01f, sfx.minDistance);
        _audio.maxDistance = Mathf.Max(_audio.minDistance + 0.01f, sfx.maxDistance);

        _audio.rolloffMode = sfx.rolloffMode;

        if (_audio.rolloffMode == AudioRolloffMode.Custom)
        {
            _audio.SetCustomCurve(AudioSourceCurveType.CustomRolloff, sfx.customRolloff);
        }
#if UNITY_EDITOR
        Debug.Log($"[WeaponSound] Applied 3D? use3D={sfx.use3D}, min={_audio.minDistance}, max={_audio.maxDistance}, mode={_audio.rolloffMode}", this);
#endif

        // �ѼҸ��� ���÷��� ������ 0 ��õ(����)
        // _audio.dopplerLevel = 0f;
    }

    // =========================
    // (A) Weapon.cs���� ���� ȣ��
    // =========================
    public void PlayEmptyTriggerSound()
    {
        if (!IsOwnerSoundSource()) return;
        var localPlayer = NetworkPlayer.Local;
        if (localPlayer != null && localPlayer.TryGetComponent<PlayerFeedbackAudio>(out var feedback))
            feedback.PlayDryFire();
    }

    public void PlayFireSound()
    {
        if (_weapon == null || _weapon.WeaponData == null) return;
        if (!EnsureAudioSourceBinding()) return;

        var sfx = _weapon.WeaponData.sfx;
        if (sfx.fireSounds == null || sfx.fireSounds.Count == 0) return;

        // pitch�� AudioSource�� ����(PlayOneShot�� pitch �Ķ���Ͱ� ����)
        _audio.pitch = Random.Range(sfx.firePitchRange.x, sfx.firePitchRange.y);

        // volume�� PlayOneShot�� volumeScale�� ó��(������ҽ� volume�� �ǵ帮�� ����)
        float volumeScale = Random.Range(sfx.fireVolumeRange.x, sfx.fireVolumeRange.y) * sfx.fireVolumeMultiplier;

        var clip = sfx.fireSounds[Random.Range(0, sfx.fireSounds.Count)];
        if (clip != null)
        {
            _audio.PlayOneShot(clip, volumeScale);
        }
    }

    // =========================
    // (B) �ִϸ��̼� �̺�Ʈ��
    // �̺�Ʈ �̸��� PlayWeaponSound ��� ������ �����ϰ� ����
    // =========================
    public void PlayWeaponSound()
    {
        // Legacy animation callback: Weapon owns full reload playback.
    }

    public void PlayWeaponSound(int _ignoredIndex)
    {
        // Keep existing animation events valid without replaying the full clip.
    }

    public void PlayReloadSound()
    {
        if (_weapon == null || _weapon.WeaponData == null) return;
        if (!EnsureAudioSourceBinding()) return;

        var clip = _weapon.WeaponData.sfx.reloadSound;
        if (clip != null)
        {
            if (IsOwnerSoundSource()) _localReloadCount++;
            else _remoteReloadCount++;

            _lastReloadPlaybackDebug = $"played owner={IsOwnerSoundSource()} clip={clip.name} local={_localReloadCount} remote={_remoteReloadCount}";

            _audio.pitch = 1f;
            _audio.PlayOneShot(clip, _weapon.WeaponData.sfx.reloadVolumeMultiplier);
        }
    }

    public string GetDebugSummary()
    {
        string groupName = _audio != null && _audio.outputAudioMixerGroup != null ? _audio.outputAudioMixerGroup.name : "null";
        return $"name={name} owner={IsOwnerSoundSource()} localReloads={_localReloadCount} remoteReloads={_remoteReloadCount} hasAudio={(_audio != null)} group={groupName}";
    }

    public int GetLocalReloadCount() => _localReloadCount;
    public int GetRemoteReloadCount() => _remoteReloadCount;
    public static string GetLastReloadPlaybackDebug() => _lastReloadPlaybackDebug;

    private bool IsOwnerSoundSource()
    {
        var net = GetComponentInParent<NetworkBehaviour>() ?? transform.root.GetComponentInChildren<NetworkBehaviour>(true);
        return net != null && net.isOwner;
    }
}
