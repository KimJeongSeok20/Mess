using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerAudioRouter : MonoBehaviour
{
    [Header("Audio Sources")]
    [SerializeField] private AudioSource playerAudioSource;
    [SerializeField] private AudioSource weaponAudioSource;

    public AudioSource PlayerAudioSource => playerAudioSource;
    public AudioSource WeaponAudioSource => weaponAudioSource;

    private void Awake()
    {
        var sources = GetComponents<AudioSource>();

        if (playerAudioSource == null)
        {
            playerAudioSource = sources.Length > 0
                ? sources[0]
                : GetComponentInChildren<AudioSource>(true);
        }

        if (weaponAudioSource == null)
        {
            if (sources.Length > 1)
            {
                weaponAudioSource = sources[1];
            }
            else if (sources.Length == 1)
            {
                weaponAudioSource = sources[0];
            }
        }
    }
}
