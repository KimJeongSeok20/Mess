using UnityEngine;

/// <summary>Scene-owned score and indoor room tone; the existing weather system owns outdoor ambience.</summary>
[DisallowMultipleComponent]
public sealed class GameBackgroundAudio : MonoBehaviour
{
    [SerializeField] private bool titleScreen;
    [SerializeField] private DungeonZoneManager dungeonZone;
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource roomToneSource;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.16f;
    [SerializeField, Range(0f, 1f)] private float roomToneVolume = 0.12f;
    [SerializeField, Min(0.1f)] private float fadeSeconds = 3f;

    private void OnEnable()
    {
        if (musicSource != null) musicSource.volume = 0f;
        if (roomToneSource != null) roomToneSource.volume = 0f;
    }

    private void Update()
    {
        bool indoors = dungeonZone != null && dungeonZone.IsInDungeon;
        Fade(musicSource, titleScreen || indoors ? musicVolume : 0f, musicVolume);
        Fade(roomToneSource, !titleScreen && indoors ? roomToneVolume : 0f, roomToneVolume);
    }

    private void Fade(AudioSource source, float target, float fullVolume)
    {
        if (source == null || source.clip == null) return;
        if (target > 0f && !source.isPlaying) source.Play();
        source.volume = Mathf.MoveTowards(source.volume, target,
            Mathf.Max(0.01f, fullVolume) * Time.unscaledDeltaTime / Mathf.Max(0.1f, fadeSeconds));
        if (target == 0f && source.volume <= 0f && source.isPlaying) source.Stop();
    }

    private void OnDisable()
    {
        if (musicSource != null) musicSource.Stop();
        if (roomToneSource != null) roomToneSource.Stop();
    }
}
