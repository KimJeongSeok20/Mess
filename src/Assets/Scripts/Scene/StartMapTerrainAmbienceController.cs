using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

[DisallowMultipleComponent]
public sealed class StartMapTerrainAmbienceController : MonoBehaviour
{
    public enum TimePeriod
    {
        Dawn,
        Day,
        Evening,
        Night,
    }

    [System.Serializable]
    private struct AmbienceEntry
    {
        public TimePeriod timePeriod;
        public StartMapWeatherController.PrecipitationMode weatherMode;
        public AudioClip clip;
    }

    [Header("References")]
    [SerializeField] private StartMapWeatherController weatherController;
    [SerializeField] private TimeManager timeManager;
    [SerializeField] private DungeonZoneManager dungeonZoneManager;
    [SerializeField] private AudioMixerGroup environmentMixerGroup;

    [Header("Time Periods")]
    [SerializeField] [Range(0f, 24f)] private float dawnStartsAt = 5f;
    [SerializeField] [Range(0f, 24f)] private float dayStartsAt = 8f;
    [SerializeField] [Range(0f, 24f)] private float eveningStartsAt = 17f;
    [SerializeField] [Range(0f, 24f)] private float nightStartsAt = 20f;

    [Header("Playback")]
    [SerializeField] [Min(0.1f)] private float evaluationInterval = 0.5f;
    [SerializeField] [Min(0f)] private float fadeTime = 2f;
    [SerializeField] private AmbienceEntry[] ambienceEntries;

    private float _evaluationTimer;
    private TimePeriod _currentTimePeriod;
    private StartMapWeatherController.PrecipitationMode _currentWeatherMode = StartMapWeatherController.PrecipitationMode.Off;
    private bool _isMutedForDungeon;
    private AudioClip _currentClip;
    private AudioClip _requestedClip;
    private AudioSource _activeSource;
    private AudioSource _idleSource;
    private Coroutine _transitionCoroutine;

    public string CurrentSoundName
    {
        get
        {
            if (_currentClip != null)
                return _currentClip.name;

            if (_activeSource != null && _activeSource.clip != null)
                return _activeSource.clip.name;

            if (_idleSource != null && _idleSource.clip != null)
                return _idleSource.clip.name;

            return string.Empty;
        }
    }
    public bool IsMutedForDungeon => _isMutedForDungeon;

    private void Awake()
    {
        EnsureSetup();

        if (Application.isPlaying)
            ApplyAmbience(forceRefresh: true);
    }

    private void OnEnable()
    {
        EnsureSetup();

        if (Application.isPlaying)
            ApplyAmbience(forceRefresh: true);
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        _evaluationTimer += Time.deltaTime;
        if (_evaluationTimer < evaluationInterval)
            return;

        _evaluationTimer = 0f;
        EnsureSetup();
        ApplyAmbience(forceRefresh: false);
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
        {
            if (_transitionCoroutine != null)
            {
                StopCoroutine(_transitionCoroutine);
                _transitionCoroutine = null;
            }

            StopSource(_activeSource);
            StopSource(_idleSource);
            _currentClip = null;
        }

        _isMutedForDungeon = false;
    }

    public void RefreshNow()
    {
        if (!Application.isPlaying)
            return;

        EnsureSetup();
        ApplyAmbience(forceRefresh: false);
    }

    public void SetOutdoorMuted(bool isMuted)
    {
        _isMutedForDungeon = isMuted;
        SetAllSourceMute(isMuted);
    }

    private void EnsureSetup()
    {
        CacheReferences();
        EnsureCrossfadeSources();
    }

    private void CacheReferences()
    {
        if (weatherController == null)
            weatherController = FindFirstObjectByType<StartMapWeatherController>();

        if (timeManager == null)
            timeManager = FindFirstObjectByType<TimeManager>();

        if (dungeonZoneManager == null)
            dungeonZoneManager = FindFirstObjectByType<DungeonZoneManager>();
    }

    private void EnsureCrossfadeSources()
    {
        AudioSource[] sources = GetComponents<AudioSource>();
        int existingCount = sources.Length;
        if (sources.Length < 2)
        {
            int countToAdd = 2 - sources.Length;
            for (int i = 0; i < countToAdd; i++)
                gameObject.AddComponent<AudioSource>();

            sources = GetComponents<AudioSource>();
        }

        _activeSource = sources[0];
        _idleSource = sources[1];

        if (existingCount < 1)
            ConfigureSource(_activeSource);
        else
            ConfigureRouting(_activeSource);

        if (existingCount < 2)
            ConfigureSource(_idleSource);
        else
            ConfigureRouting(_idleSource);
    }

    private void SetAllSourceMute(bool isMuted)
    {
        AudioSource[] sources = GetComponents<AudioSource>();
        for (int i = 0; i < sources.Length; i++)
        {
            AudioSource source = sources[i];
            if (source != null)
                source.mute = isMuted;
        }
    }

    private void ConfigureSource(AudioSource source)
    {
        if (source == null)
            return;

        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.dopplerLevel = 0f;
        ConfigureRouting(source);
    }

    private void ConfigureRouting(AudioSource source)
    {
        if (source == null)
            return;

        source.outputAudioMixerGroup = environmentMixerGroup;
    }

    private void ApplyAmbience(bool forceRefresh)
    {
        bool isInDungeon = dungeonZoneManager != null && dungeonZoneManager.IsInDungeon;
        _isMutedForDungeon = isInDungeon;

        if (isInDungeon)
        {
            SetAllSourceMute(true);

            return;
        }

        SetAllSourceMute(false);

        TimePeriod nextTimePeriod = ResolveTimePeriod();
        StartMapWeatherController.PrecipitationMode nextWeatherMode = ResolveWeatherMode();
        AudioClip nextClip = ResolveClip(nextTimePeriod, nextWeatherMode);

        bool stateChanged = forceRefresh
            || nextTimePeriod != _currentTimePeriod
            || nextWeatherMode != _currentWeatherMode
            || _requestedClip != nextClip;

        if (!stateChanged)
            return;

        _currentTimePeriod = nextTimePeriod;
        _currentWeatherMode = nextWeatherMode;
        TransitionToClip(nextClip);
    }

    private void TransitionToClip(AudioClip nextClip)
    {
        _requestedClip = nextClip;

        if (_transitionCoroutine != null)
            StopCoroutine(_transitionCoroutine);

        _transitionCoroutine = StartCoroutine(TransitionCoroutine(nextClip));
    }

    private IEnumerator TransitionCoroutine(AudioClip nextClip)
    {
        AudioSource outgoingSource = _activeSource;
        AudioSource incomingSource = _idleSource;

        if (nextClip == null)
        {
            float startVolume = outgoingSource != null ? outgoingSource.volume : 0f;
            yield return FadeSource(outgoingSource, startVolume, 0f);
            StopSource(outgoingSource);
            _currentClip = null;
            _requestedClip = null;
            yield break;
        }

        if (outgoingSource != null && outgoingSource.isPlaying && outgoingSource.clip == nextClip)
        {
            _currentClip = nextClip;
            yield break;
        }

        _currentClip = nextClip;

        if (incomingSource != null)
        {
            incomingSource.mute = _isMutedForDungeon;
            incomingSource.clip = nextClip;
            incomingSource.outputAudioMixerGroup = environmentMixerGroup;
            incomingSource.volume = 0f;

            if (!incomingSource.isPlaying)
                incomingSource.Play();
        }

        float outgoingStartVolume = outgoingSource != null ? outgoingSource.volume : 0f;
        yield return CrossFade(outgoingSource, outgoingStartVolume, incomingSource);

        StopSource(outgoingSource);

        AudioSource previousActive = _activeSource;
        _activeSource = incomingSource;
        _idleSource = previousActive;
    }

    private IEnumerator CrossFade(AudioSource outgoingSource, float outgoingStartVolume, AudioSource incomingSource)
    {
        if (fadeTime <= 0f)
        {
            if (outgoingSource != null)
                outgoingSource.volume = 0f;

            if (incomingSource != null)
                incomingSource.volume = 1f;

            yield break;
        }

        float elapsed = 0f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeTime);

            if (outgoingSource != null)
                outgoingSource.volume = Mathf.Lerp(outgoingStartVolume, 0f, t);

            if (incomingSource != null)
                incomingSource.volume = Mathf.Lerp(0f, 1f, t);

            yield return null;
        }

        if (outgoingSource != null)
            outgoingSource.volume = 0f;

        if (incomingSource != null)
            incomingSource.volume = 1f;
    }

    private IEnumerator FadeSource(AudioSource source, float startVolume, float endVolume)
    {
        if (source == null)
            yield break;

        if (fadeTime <= 0f)
        {
            source.volume = endVolume;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeTime);
            source.volume = Mathf.Lerp(startVolume, endVolume, t);
            yield return null;
        }

        source.volume = endVolume;
    }

    private static void StopSource(AudioSource source)
    {
        if (source == null)
            return;

        source.mute = true;
        source.Stop();
        source.clip = null;
        source.volume = 0f;
    }

    private TimePeriod ResolveTimePeriod()
    {
        float currentHour = ResolveCurrentHour();

        if (currentHour >= nightStartsAt || currentHour < dawnStartsAt)
            return TimePeriod.Night;

        if (currentHour >= eveningStartsAt)
            return TimePeriod.Evening;

        if (currentHour >= dayStartsAt)
            return TimePeriod.Day;

        return TimePeriod.Dawn;
    }

    private float ResolveCurrentHour()
    {
        if (timeManager != null)
            return NormalizeHour(timeManager.GetCurrentTime());

        if (weatherController != null)
            return NormalizeHour(weatherController.CurrentResolvedTime);

        return 0f;
    }

    private StartMapWeatherController.PrecipitationMode ResolveWeatherMode()
    {
        if (weatherController == null)
            return StartMapWeatherController.PrecipitationMode.Off;

        if (weatherController.IsRainVfxActive)
            return StartMapWeatherController.PrecipitationMode.Rain;

        if (weatherController.IsSnowVfxActive)
            return StartMapWeatherController.PrecipitationMode.Snow;

        return StartMapWeatherController.PrecipitationMode.Off;
    }

    private AudioClip ResolveClip(TimePeriod timePeriod, StartMapWeatherController.PrecipitationMode weatherMode)
    {
        if (ambienceEntries == null)
            return null;

        for (int i = 0; i < ambienceEntries.Length; i++)
        {
            AmbienceEntry entry = ambienceEntries[i];
            if (entry.timePeriod == timePeriod && entry.weatherMode == weatherMode)
                return entry.clip;
        }

        if (weatherMode != StartMapWeatherController.PrecipitationMode.Off)
        {
            for (int i = 0; i < ambienceEntries.Length; i++)
            {
                AmbienceEntry entry = ambienceEntries[i];
                if (entry.timePeriod == timePeriod && entry.weatherMode == StartMapWeatherController.PrecipitationMode.Off)
                    return entry.clip;
            }
        }

        return null;
    }

    private static float NormalizeHour(float hour)
    {
        if (hour < 0f)
            hour %= 24f;

        hour %= 24f;
        if (hour < 0f)
            hour += 24f;

        return hour;
    }
}
