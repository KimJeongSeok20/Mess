using OccaSoftware.Altos.Runtime;
using UnityEngine;
using PurrNet;

[ExecuteAlways]
public class StartMapWeatherController : NetworkBehaviour
{
    public enum PrecipitationMode
    {
        Automatic,
        Off,
        Rain,
        Snow,
    }

    [Header("References")]
    [SerializeField] private AltosSkyDirector skyDirector;
    [SerializeField] private Transform weatherOrigin;
    [SerializeField] private WeatherManager weatherZone;
    [SerializeField] private DungeonZoneManager dungeonZoneManager;
    [SerializeField] private GameObject rainEffectRoot;
    [SerializeField] private GameObject snowEffectRoot;

    [Header("Manual Clouds")]
    [SerializeField] private bool useDynamicCloudiness;
    [SerializeField] [Range(0f, 1f)] private float cloudiness = 0.58f;
    [SerializeField] [Range(-1f, 1f)] private float cloudinessMin = 0.35f;
    [SerializeField] [Range(-1f, 1f)] private float cloudinessMax = 0.75f;

    [Header("Manual Precipitation")]
    [SerializeField] private PrecipitationMode precipitationMode = PrecipitationMode.Automatic;
    [SerializeField] [Range(0f, 1f)] private float precipitationIntensity = 0.6f;
    [SerializeField] [Min(0f)] private float weatherRadius = 6000f;

    [Header("Temperature")]
    [SerializeField] private bool overrideTemperatureFromWeather = true;
    [SerializeField] private float rainSeaLevelTemperature = 8f;
    [SerializeField] private float snowSeaLevelTemperature = -6f;

    [Header("Automatic Precipitation")]
    [SerializeField] [Range(0f, 1f)] private float precipitationStartsAtCloudiness = 0.72f;
    [SerializeField] [Range(0f, 1f)] private float precipitationMaxAtCloudiness = 0.9f;
    [SerializeField] [Min(0.01f)] private float cloudinessResponseSpeed = 0.06f;
    [SerializeField] [Range(0f, 0.15f)] private float cloudinessDriftAmplitude = 0.02f;
    [SerializeField] [Min(0.001f)] private float cloudinessDriftSpeed = 0.006f;
    [SerializeField] [Min(0.01f)] private float precipitationResponseSpeed = 0.22f;
    [SerializeField] private float snowTemperatureThreshold = 0f;
    [SerializeField] [Min(0.01f)] private float snowTemperatureBlendRange = 1.5f;

    [Header("Runtime")]
    [SerializeField] private bool bindRuntimeCamera = true;
    [SerializeField] private Vector3 rainCameraOffset = new Vector3(0f, 7f, 0f);
    [SerializeField] private Vector3 snowCameraOffset = new Vector3(0f, 2.5f, 0f);
    [SerializeField] private bool bindCollisionCamera;

    [Header("Local VFX Suppression")]
    [SerializeField] private bool suppressLocalPrecipitationInDungeon = true;
    [SerializeField] private bool suppressLocalPrecipitationUnderCover = true;
    [SerializeField] private LayerMask shelterBlockerLayers = ~0;
    [SerializeField] [Min(0.05f)] private float shelterCheckRadius = 0.35f;
    [SerializeField] [Min(0.1f)] private float shelterCheckDistance = 7f;
    [SerializeField] private Vector3 shelterCheckOffset = new Vector3(0f, 0.15f, 0f);

    private Camera _boundCamera;
    private PrecipitationMode _lastAutomaticPrecipitationMode = PrecipitationMode.Rain;
    private float _currentAutomaticCloudiness;
    private float _currentAutomaticPrecipitationIntensity;
    private int _sessionWeatherSeed;
    private float _sessionWeatherTimeOffset;
    private bool _sessionWeatherInitialized;
    private bool _automaticCloudinessInitialized;
    private bool _timeManagerSubscribed;
    private int _weatherDayIndex;

    private readonly SyncVar<int> _syncedResolvedPrecipitationMode = new((int)PrecipitationMode.Off, ownerAuth: false);
    private readonly SyncVar<float> _syncedResolvedPrecipitationIntensity = new(0f, ownerAuth: false);
    private readonly SyncVar<float> _syncedResolvedCloudiness = new(0f, ownerAuth: false);

    public PrecipitationMode CurrentResolvedPrecipitationMode { get; private set; } = PrecipitationMode.Off;
    public float CurrentResolvedPrecipitationIntensity { get; private set; }
    public float CurrentResolvedCloudiness { get; private set; }
    public float CurrentResolvedTime => ResolveSystemTime();
    public bool IsRainVfxActive => rainEffectRoot != null && rainEffectRoot.activeSelf;
    public bool IsSnowVfxActive => snowEffectRoot != null && snowEffectRoot.activeSelf;

    protected override void OnSpawned()
    {
        base.OnSpawned();

        _syncedResolvedPrecipitationMode.onChanged += OnSyncedPrecipitationModeChanged;
        _syncedResolvedPrecipitationIntensity.onChanged += OnSyncedPrecipitationIntensityChanged;
        _syncedResolvedCloudiness.onChanged += OnSyncedCloudinessChanged;

        if (Application.isPlaying)
            ApplySettings();
    }

    protected override void OnDespawned()
    {
        _syncedResolvedPrecipitationMode.onChanged -= OnSyncedPrecipitationModeChanged;
        _syncedResolvedPrecipitationIntensity.onChanged -= OnSyncedPrecipitationIntensityChanged;
        _syncedResolvedCloudiness.onChanged -= OnSyncedCloudinessChanged;
        base.OnDespawned();
    }

    private void Awake()
    {
        InitializeSessionWeather();
        CacheReferences();
        RegisterTimeEvents();
        ApplySettings();
    }

    private void OnEnable()
    {
        InitializeSessionWeather();
        CacheReferences();
        RegisterTimeEvents();
        ApplySettings();
    }

    private void InitializeSessionWeather()
    {
        if (_sessionWeatherInitialized || !Application.isPlaying)
            return;

        if (NetworkManager.main != null && !isServer)
            return;

        _sessionWeatherSeed = System.Guid.NewGuid().GetHashCode();
        _sessionWeatherTimeOffset = GetWeatherTimeOffset(_weatherDayIndex);
        _sessionWeatherInitialized = true;
    }

    private void RegisterTimeEvents()
    {
        if (!Application.isPlaying || _timeManagerSubscribed)
            return;

        TimeManager.OnDayReset += HandleDayReset;
        _timeManagerSubscribed = true;
    }

    private void HandleDayReset()
    {
        if (Application.isPlaying && NetworkManager.main != null && !isServer)
            return;

        _weatherDayIndex++;
        _sessionWeatherTimeOffset = GetWeatherTimeOffset(_weatherDayIndex);
    }

    private void OnValidate()
    {
        if (cloudinessMin > cloudinessMax)
            cloudinessMax = cloudinessMin;

        if (precipitationStartsAtCloudiness > precipitationMaxAtCloudiness)
            precipitationMaxAtCloudiness = precipitationStartsAtCloudiness;

        CacheReferences();
        ApplySettings();
    }

    private void Update()
    {
        CacheReferences();
        ApplySettings();

        if (!Application.isPlaying || !bindRuntimeCamera)
            return;

        Camera targetCamera = FindRuntimeCamera();
        if (targetCamera == null || targetCamera == _boundCamera)
            return;

        BindCamera(targetCamera);
    }

    private void OnDisable()
    {
        if (_timeManagerSubscribed)
        {
            TimeManager.OnDayReset -= HandleDayReset;
            _timeManagerSubscribed = false;
        }

        if (!Application.isPlaying || skyDirector == null)
            return;

        skyDirector.cloudDefinition?.ReleaseOverride();
        skyDirector.precipitationDefinition?.ReleaseOverride();
        skyDirector.temperatureDefinition?.ReleaseOverride();
    }

    private void CacheReferences()
    {
        if (skyDirector == null)
            skyDirector = GetComponentInChildren<AltosSkyDirector>(true);

        if (weatherOrigin == null)
            weatherOrigin = transform;

        if (weatherZone == null)
            weatherZone = GetComponentInChildren<WeatherManager>(true);

        if (dungeonZoneManager == null && Application.isPlaying)
            dungeonZoneManager = FindFirstObjectByType<DungeonZoneManager>();
    }

    private void ApplySettings()
    {
        float resolvedCloudiness;
        PrecipitationMode resolvedMode;
        float resolvedPrecipitationIntensity;

        if (Application.isPlaying && NetworkManager.main != null && !isServer)
        {
            resolvedCloudiness = _syncedResolvedCloudiness.value;
            resolvedMode = (PrecipitationMode)_syncedResolvedPrecipitationMode.value;
            resolvedPrecipitationIntensity = _syncedResolvedPrecipitationIntensity.value;
        }
        else
        {
            resolvedCloudiness = ResolveCloudiness();
            resolvedMode = precipitationMode;
            resolvedPrecipitationIntensity = precipitationIntensity;

            if (precipitationMode == PrecipitationMode.Automatic)
                ResolveAutomaticPrecipitation(resolvedCloudiness, out resolvedMode, out resolvedPrecipitationIntensity);

            if (Application.isPlaying && NetworkManager.main != null && isServer)
                SyncResolvedWeather(resolvedMode, resolvedPrecipitationIntensity, resolvedCloudiness);
        }

        CurrentResolvedCloudiness = resolvedCloudiness;
        CurrentResolvedPrecipitationMode = resolvedMode;
        CurrentResolvedPrecipitationIntensity = resolvedPrecipitationIntensity;

        ApplyCloudSettings(resolvedCloudiness);
        ApplyPrecipitationSettings(resolvedMode, resolvedPrecipitationIntensity);
        ApplyTemperatureSettings(resolvedMode);

        bool suppressLocalPrecipitation = ShouldSuppressLocalPrecipitation();
        bool enableRain = resolvedMode == PrecipitationMode.Rain && resolvedPrecipitationIntensity > 0.001f;
        bool enableSnow = resolvedMode == PrecipitationMode.Snow && resolvedPrecipitationIntensity > 0.001f;

        SetActive(rainEffectRoot, enableRain && !suppressLocalPrecipitation);
        SetActive(snowEffectRoot, enableSnow && !suppressLocalPrecipitation);
    }

    private void ApplyCloudSettings(float resolvedCloudiness)
    {
        if (skyDirector == null || skyDirector.cloudDefinition == null)
            return;

        if (!Application.isPlaying)
            return;

        CloudDefinition cloudDefinition = skyDirector.cloudDefinition;
        if (precipitationMode == PrecipitationMode.Automatic || !useDynamicCloudiness)
            cloudDefinition.OverrideCloudiness(resolvedCloudiness);
        else
            cloudDefinition.ReleaseOverride();
    }

    private void ApplyPrecipitationSettings(PrecipitationMode resolvedMode, float resolvedPrecipitationIntensity)
    {
        if (weatherZone != null)
        {
            if (weatherOrigin != null)
                weatherZone.transform.position = weatherOrigin.position;

            weatherZone.radius = weatherRadius;
            weatherZone.precipitationIntensity = resolvedMode == PrecipitationMode.Off ? 0f : resolvedPrecipitationIntensity;
        }

        if (skyDirector == null || skyDirector.precipitationDefinition == null)
            return;

        if (!Application.isPlaying)
            return;

        float globalPrecipitation = resolvedMode == PrecipitationMode.Off ? 0f : resolvedPrecipitationIntensity;
        skyDirector.precipitationDefinition.OverridePrecipitation(globalPrecipitation);
    }

    private void ApplyTemperatureSettings(PrecipitationMode resolvedMode)
    {
        if (skyDirector == null || skyDirector.temperatureDefinition == null || !Application.isPlaying)
            return;

        if (NetworkManager.main != null && !isServer)
        {
            if (!overrideTemperatureFromWeather)
            {
                skyDirector.temperatureDefinition.ReleaseOverride();
                return;
            }

            switch (resolvedMode)
            {
                case PrecipitationMode.Snow:
                    skyDirector.temperatureDefinition.OverrideTemperature(snowSeaLevelTemperature);
                    break;
                case PrecipitationMode.Rain:
                    skyDirector.temperatureDefinition.OverrideTemperature(rainSeaLevelTemperature);
                    break;
                default:
                    skyDirector.temperatureDefinition.ReleaseOverride();
                    break;
            }

            return;
        }

        if (precipitationMode == PrecipitationMode.Automatic)
        {
            float sampledTemp = SampleCurrentSeaLevelTemperature();
            skyDirector.temperatureDefinition.OverrideTemperature(sampledTemp);
            return;
        }

        if (!overrideTemperatureFromWeather)
        {
            skyDirector.temperatureDefinition.ReleaseOverride();
            return;
        }

        switch (resolvedMode)
        {
            case PrecipitationMode.Snow:
                skyDirector.temperatureDefinition.OverrideTemperature(snowSeaLevelTemperature);
                break;
            case PrecipitationMode.Rain:
                skyDirector.temperatureDefinition.OverrideTemperature(rainSeaLevelTemperature);
                break;
            default:
                skyDirector.temperatureDefinition.ReleaseOverride();
                break;
        }
    }

    private void BindCamera(Camera targetCamera)
    {
        _boundCamera = targetCamera;
        AssignCamera(rainEffectRoot, targetCamera, rainCameraOffset);
        AssignCamera(snowEffectRoot, targetCamera, snowCameraOffset);
    }

    private void AssignCamera(GameObject effectRoot, Camera targetCamera, Vector3 followOffset)
    {
        if (effectRoot == null || targetCamera == null)
            return;

        WeatherEffect[] effects = effectRoot.GetComponentsInChildren<WeatherEffect>(true);
        for (int i = 0; i < effects.Length; i++)
        {
            effects[i].cameraFollowSettings.mainCamera = targetCamera;
            effects[i].cameraFollowSettings.positionOffset = followOffset;

            if (bindCollisionCamera)
                effects[i].precipitationCollisionSettings.camera = targetCamera;
        }
    }

    private static Camera FindRuntimeCamera()
    {
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Camera bestCamera = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cameraComponent = cameras[i];
            if (!cameraComponent.enabled || !cameraComponent.gameObject.activeInHierarchy)
                continue;

            if (cameraComponent.cameraType != CameraType.Game)
                continue;

            int score = 0;
            if (cameraComponent.CompareTag("MainCamera"))
                score += 4;

            if (cameraComponent.TryGetComponent(out AudioListener audioListener) && audioListener.enabled)
                score += 3;

            if (score > bestScore)
            {
                bestScore = score;
                bestCamera = cameraComponent;
            }
        }

        return bestCamera;
    }

    private void ResolveAutomaticPrecipitation(
        float resolvedCloudiness,
        out PrecipitationMode resolvedMode,
        out float resolvedPrecipitationIntensity
    )
    {
        float targetPrecipitationIntensity = Mathf.InverseLerp(
            precipitationStartsAtCloudiness,
            precipitationMaxAtCloudiness,
            resolvedCloudiness
        );
        targetPrecipitationIntensity = Mathf.Clamp01(targetPrecipitationIntensity * targetPrecipitationIntensity);

        if (Application.isPlaying)
        {
            _currentAutomaticPrecipitationIntensity = Mathf.MoveTowards(
                _currentAutomaticPrecipitationIntensity,
                targetPrecipitationIntensity,
                precipitationResponseSpeed * Time.deltaTime
            );
        }
        else
        {
            _currentAutomaticPrecipitationIntensity = targetPrecipitationIntensity;
        }

        resolvedPrecipitationIntensity = _currentAutomaticPrecipitationIntensity;

        if (resolvedPrecipitationIntensity <= 0.001f)
        {
            resolvedMode = PrecipitationMode.Off;
            resolvedPrecipitationIntensity = 0f;
            return;
        }

        float currentSeaLevelTemperature = SampleCurrentSeaLevelTemperature();
        float snowWeight = Mathf.InverseLerp(
            snowTemperatureThreshold + snowTemperatureBlendRange,
            snowTemperatureThreshold - snowTemperatureBlendRange,
            currentSeaLevelTemperature
        );

        if (snowWeight >= 0.6f)
            resolvedMode = PrecipitationMode.Snow;
        else if (snowWeight <= 0.4f)
            resolvedMode = PrecipitationMode.Rain;
        else
            resolvedMode = _lastAutomaticPrecipitationMode == PrecipitationMode.Off
                ? PrecipitationMode.Rain
                : _lastAutomaticPrecipitationMode;

        _lastAutomaticPrecipitationMode = resolvedMode;
    }

    private float ResolveCloudiness()
    {
        if (precipitationMode == PrecipitationMode.Automatic)
        {
            if (skyDirector != null && skyDirector.cloudDefinition != null)
            {
                float targetCloudiness = Mathf.Clamp01(skyDirector.cloudDefinition.GetCloudinessAtTime(GetWeatherSamplingTime()));
                targetCloudiness = ApplyCloudinessDrift(targetCloudiness);

                if (!_automaticCloudinessInitialized || !Application.isPlaying)
                {
                    _currentAutomaticCloudiness = targetCloudiness;
                    _automaticCloudinessInitialized = true;
                }
                else
                {
                    _currentAutomaticCloudiness = Mathf.MoveTowards(
                        _currentAutomaticCloudiness,
                        targetCloudiness,
                        cloudinessResponseSpeed * Time.deltaTime
                    );
                }

                return _currentAutomaticCloudiness;
            }

            return cloudiness;
        }

        if (!useDynamicCloudiness)
            return cloudiness;

        if (skyDirector != null && skyDirector.cloudDefinition != null)
            return Mathf.Clamp01(skyDirector.cloudDefinition.GetCloudinessAtTime(ResolveSystemTime()));

        return Mathf.Clamp01(Mathf.Lerp(cloudinessMin, cloudinessMax, 0.5f));
    }

    private float ApplyCloudinessDrift(float targetCloudiness)
    {
        if (!Application.isPlaying || cloudinessDriftAmplitude <= 0f)
            return targetCloudiness;

        float noiseX = Mathf.Abs(_sessionWeatherSeed) * 0.001f + _weatherDayIndex * 17.13f;
        float noiseY = Time.unscaledTime * cloudinessDriftSpeed;
        float drift = Mathf.PerlinNoise(noiseX, noiseY) * 2f - 1f;
        return Mathf.Clamp01(targetCloudiness + drift * cloudinessDriftAmplitude);
    }

    private float SampleCurrentSeaLevelTemperature()
    {
        if (skyDirector == null || skyDirector.temperatureDefinition == null)
            return rainSeaLevelTemperature;

        return skyDirector.temperatureDefinition.GetTemperatureAtTime(GetWeatherSamplingTime());
    }

    private float GetWeatherSamplingTime()
    {
        return ResolveSystemTime() + _sessionWeatherTimeOffset;
    }

    private float GetWeatherTimeOffset(int dayIndex)
    {
        var sessionRandom = new System.Random(_sessionWeatherSeed + dayIndex * 7919);
        return (float)(sessionRandom.NextDouble() * 100000.0);
    }

    private float ResolveSystemTime()
    {
        if (skyDirector != null && skyDirector.skyDefinition != null)
            return skyDirector.skyDefinition.timeSystem;

        return 0f;
    }

    private bool ShouldSuppressLocalPrecipitation()
    {
        return IsInsideDungeon() || IsUnderCover();
    }

    private bool IsInsideDungeon()
    {
        return suppressLocalPrecipitationInDungeon
            && dungeonZoneManager != null
            && dungeonZoneManager.IsInDungeon;
    }

    private bool IsUnderCover()
    {
        if (!suppressLocalPrecipitationUnderCover || !Application.isPlaying)
            return false;

        Transform weatherView = _boundCamera != null ? _boundCamera.transform : weatherOrigin;
        if (weatherView == null)
            return false;

        Vector3 origin = weatherView.position + shelterCheckOffset;
        return Physics.SphereCast(
            origin,
            shelterCheckRadius,
            Vector3.up,
            out _,
            shelterCheckDistance,
            shelterBlockerLayers,
            QueryTriggerInteraction.Ignore
        );
    }

    private static void SetActive(GameObject target, bool value)
    {
        if (target != null && target.activeSelf != value)
            target.SetActive(value);
    }

    private void SyncResolvedWeather(PrecipitationMode mode, float intensity, float cloudinessValue)
    {
        _syncedResolvedPrecipitationMode.value = (int)mode;
        _syncedResolvedPrecipitationIntensity.value = intensity;
        _syncedResolvedCloudiness.value = cloudinessValue;
    }

    private void OnSyncedPrecipitationModeChanged(int _)
    {
        if (Application.isPlaying && NetworkManager.main != null && !isServer)
            ApplySettings();
    }

    private void OnSyncedPrecipitationIntensityChanged(float _)
    {
        if (Application.isPlaying && NetworkManager.main != null && !isServer)
            ApplySettings();
    }

    private void OnSyncedCloudinessChanged(float _)
    {
        if (Application.isPlaying && NetworkManager.main != null && !isServer)
            ApplySettings();
    }

    public void SetManualWeatherMode(PrecipitationMode mode, float intensity)
    {
        if (Application.isPlaying && NetworkManager.main != null && !isServer)
        {
            RequestSetManualWeatherModeRpc((int)mode, Mathf.Clamp01(intensity));
            return;
        }

        precipitationMode = mode;
        precipitationIntensity = Mathf.Clamp01(intensity);
        ApplySettings();
    }

    public void SetManualCloudiness(float amount)
    {
        if (Application.isPlaying && NetworkManager.main != null && !isServer)
        {
            RequestSetManualCloudinessRpc(Mathf.Clamp01(amount));
            return;
        }

        precipitationMode = PrecipitationMode.Off;
        useDynamicCloudiness = false;
        cloudiness = Mathf.Clamp01(amount);
        ApplySettings();
    }

    [ServerRpc(requireOwnership: false)]
    private void RequestSetManualWeatherModeRpc(int mode, float intensity)
    {
        precipitationMode = (PrecipitationMode)mode;
        precipitationIntensity = Mathf.Clamp01(intensity);
        ApplySettings();
    }

    [ServerRpc(requireOwnership: false)]
    private void RequestSetManualCloudinessRpc(float amount)
    {
        precipitationMode = PrecipitationMode.Off;
        useDynamicCloudiness = false;
        cloudiness = Mathf.Clamp01(amount);
        ApplySettings();
    }
}
