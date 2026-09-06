using Demo.Scripts.Runtime.Character;
using Demo.Scripts.Runtime.Item;
using PurrNet;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MonsterTestSceneController : MonoBehaviour
{
    private enum MonsterVariant
    {
        Smily,
        Clown,
        Octopus
    }

    private enum TestMode
    {
        LiveCombat,
        SafeObservation
    }

    private enum EncounterRange
    {
        Close,
        Medium,
        Long
    }

    [Header("Scene Refs")]
    [SerializeField] private Transform spawnRoot;
    [SerializeField] private Transform spawnAnchor;
    [SerializeField] private Transform analysisTarget;
    [SerializeField] private Transform registryRoot;
    [SerializeField] private Transform playerSpawnPoint;

    [Header("Prefabs")]
    [SerializeField] private GameObject smilyPrefab;
    [SerializeField] private GameObject clownPrefab;
    [SerializeField] private GameObject octopusPrefab;

    [Header("UI")]
    [SerializeField] private Button smilyButton;
    [SerializeField] private Button clownButton;
    [SerializeField] private Button octopusButton;
    [SerializeField] private Button liveCombatButton;
    [SerializeField] private Button safeObservationButton;
    [SerializeField] private Button closeRangeButton;
    [SerializeField] private Button mediumRangeButton;
    [SerializeField] private Button longRangeButton;
    [SerializeField] private Button respawnButton;
    [SerializeField] private Text statusLabel;
    [SerializeField] private Text checklistLabel;
    [SerializeField] private float runtimeStatusRefreshInterval = 0.2f;

    [Header("Combat Feel Test")]
    [SerializeField] private TestMode testMode = TestMode.LiveCombat;
    [SerializeField] private EncounterRange encounterRange = EncounterRange.Close;
    [SerializeField, Min(3f)] private float closeRangeMeters = 7f;
    [SerializeField, Min(5f)] private float mediumRangeMeters = 14f;
    [SerializeField, Min(8f)] private float longRangeMeters = 22f;
    [SerializeField, Min(0.05f)] private float metricsArmDelay = 0.15f;
    [SerializeField, Min(0.1f)] private float observationTargetRecoveryInterval = 0.5f;

    private GameObject activeMonster;
    private MonsterVariant? activeVariant;
    private MonsterVariant? lastSpawnedVariant;
    private string activeDescription = string.Empty;
    private float nextRuntimeStatusRefreshAt;
    private float nextWeaponRefreshAt;
    private float nextObservationRecoveryAt;
    private float metricsArmAt;
    private float encounterStartedAt;
    private float firstDamageAt = -1f;
    private float lastDamageAt = -1f;
    private float lastDamageGap = -1f;
    private float deathAt = -1f;
    private int shotsFired;
    private int damagingEvents;
    private int totalDamage;
    private int lastHealth = -1;
    private bool metricsArmed;
    private Weapon trackedWeapon;
    private PlayerVitals observationTargetVitals;

    private void Awake()
    {
        if (playerSpawnPoint == null)
        {
            GameObject spawnPointObject = GameObject.Find("PlayerSpawnPoint");
            if (spawnPointObject != null)
                playerSpawnPoint = spawnPointObject.transform;
        }

        EnsureRuntimeCombatPanel();
        WireButton(smilyButton, SpawnSmily);
        WireButton(clownButton, SpawnClown);
        WireButton(octopusButton, SpawnOctopus);
        WireButton(liveCombatButton, SetLiveCombatMode);
        WireButton(safeObservationButton, SetSafeObservationMode);
        WireButton(closeRangeButton, SetCloseRange);
        WireButton(mediumRangeButton, SetMediumRange);
        WireButton(longRangeButton, SetLongRange);
        WireButton(respawnButton, RespawnCurrent);
        ConfigureAnalysisTarget();
        ApplyTestMode(respawnCurrent: false);
        UpdateChecklist();
    }

    private void EnsureRuntimeCombatPanel()
    {
        bool hasAuthoredPanel = liveCombatButton != null
            && safeObservationButton != null
            && closeRangeButton != null
            && mediumRangeButton != null
            && longRangeButton != null
            && respawnButton != null
            && statusLabel != null
            && checklistLabel != null;
        if (hasAuthoredPanel)
            return;

        Canvas canvas = statusLabel != null ? statusLabel.GetComponentInParent<Canvas>() : null;
        if (canvas == null)
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i] != null && canvases[i].renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    canvas = canvases[i];
                    break;
                }
            }
        }

        if (canvas == null)
            return;

        Transform legacyPanel = statusLabel != null ? statusLabel.transform.parent : null;
        if (legacyPanel != null)
            legacyPanel.gameObject.SetActive(false);

        GameObject panelObject = new GameObject("CombatFeelRuntimePanel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(canvas.transform, false);

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = new Vector2(-24f, -24f);
        panelRect.sizeDelta = new Vector2(430f, 650f);

        panelObject.GetComponent<Image>().color = new Color(0.05f, 0.08f, 0.11f, 0.88f);
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            return;

        CreateRuntimeLabel(panelObject.transform, "Title", "GUN vs MONSTER - COMBAT FEEL", font, 18, FontStyle.Bold, new Vector2(14f, -12f), new Vector2(402f, 26f), Color.white);
        liveCombatButton = CreateRuntimeButton(panelObject.transform, "LIVE COMBAT", font, new Vector2(14f, -48f), new Vector2(194f, 36f), new Color(0.55f, 0.22f, 0.18f, 1f));
        safeObservationButton = CreateRuntimeButton(panelObject.transform, "SAFE OBSERVE", font, new Vector2(222f, -48f), new Vector2(194f, 36f), new Color(0.20f, 0.40f, 0.52f, 1f));
        closeRangeButton = CreateRuntimeButton(panelObject.transform, "CLOSE 7m", font, new Vector2(14f, -92f), new Vector2(124f, 34f), new Color(0.37f, 0.33f, 0.25f, 1f));
        mediumRangeButton = CreateRuntimeButton(panelObject.transform, "MEDIUM 14m", font, new Vector2(153f, -92f), new Vector2(124f, 34f), new Color(0.32f, 0.37f, 0.28f, 1f));
        longRangeButton = CreateRuntimeButton(panelObject.transform, "LONG 22m", font, new Vector2(292f, -92f), new Vector2(124f, 34f), new Color(0.26f, 0.32f, 0.42f, 1f));
        smilyButton = CreateRuntimeButton(panelObject.transform, "SPAWN SMILY", font, new Vector2(14f, -134f), new Vector2(402f, 36f), new Color(0.27f, 0.44f, 0.35f, 1f));
        clownButton = CreateRuntimeButton(panelObject.transform, "SPAWN CLOWN", font, new Vector2(14f, -178f), new Vector2(402f, 36f), new Color(0.46f, 0.28f, 0.26f, 1f));
        octopusButton = CreateRuntimeButton(panelObject.transform, "SPAWN OCTOPUS SWARM", font, new Vector2(14f, -222f), new Vector2(402f, 36f), new Color(0.22f, 0.33f, 0.50f, 1f));
        respawnButton = CreateRuntimeButton(panelObject.transform, "RESET / RESPAWN CURRENT", font, new Vector2(14f, -266f), new Vector2(402f, 34f), new Color(0.38f, 0.30f, 0.45f, 1f));
        statusLabel = CreateRuntimeLabel(panelObject.transform, "MonsterStatusText", "Preparing combat feel test scene...", font, 13, FontStyle.Normal, new Vector2(14f, -310f), new Vector2(402f, 190f), new Color(0.89f, 0.93f, 0.97f, 1f));
        checklistLabel = CreateRuntimeLabel(panelObject.transform, "CombatFeelChecklistText", string.Empty, font, 12, FontStyle.Normal, new Vector2(14f, -510f), new Vector2(402f, 126f), new Color(0.98f, 0.82f, 0.47f, 1f));
    }

    private static Button CreateRuntimeButton(Transform parent, string label, Font font, Vector2 anchoredPosition, Vector2 size, Color color)
    {
        GameObject buttonObject = new GameObject(label.Replace(' ', '_'), typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        buttonObject.GetComponent<Image>().color = color;

        Text text = CreateRuntimeLabel(buttonObject.transform, "Label", label, font, 14, FontStyle.Bold, Vector2.zero, size, Color.white);
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(-12f, -8f);
        text.alignment = TextAnchor.MiddleCenter;

        return buttonObject.GetComponent<Button>();
    }

    private static Text CreateRuntimeLabel(Transform parent, string name, string label, Font font, int fontSize, FontStyle fontStyle, Vector2 anchoredPosition, Vector2 size, Color color)
    {
        GameObject labelObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(parent, false);

        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Text text = labelObject.GetComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = TextAnchor.UpperLeft;
        text.color = color;
        text.text = label;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private void Start()
    {
        RebuildRegistry();
        UpdateStatus(
            "Gun vs Monster Test Ready",
            "Choose a mode, range, and monster. Pick up a weapon from the rack behind the spawn point."
        );
    }

    private void Update()
    {
        ProcessHotkeys();
        TickObservationTargetRecovery();
        RefreshTrackedWeapon();
        TrackCombatMetrics();

        if (Time.time >= nextRuntimeStatusRefreshAt)
        {
            nextRuntimeStatusRefreshAt = Time.time + Mathf.Max(0.05f, runtimeStatusRefreshInterval);
            RefreshRuntimeStatus();
        }
    }

    private void ProcessHotkeys()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.f1Key.wasPressedThisFrame) SpawnSmily();
        if (keyboard.f2Key.wasPressedThisFrame) SpawnClown();
        if (keyboard.f3Key.wasPressedThisFrame) SpawnOctopus();
        if (keyboard.f5Key.wasPressedThisFrame) SetLiveCombatMode();
        if (keyboard.f6Key.wasPressedThisFrame) SetSafeObservationMode();
        if (keyboard.f7Key.wasPressedThisFrame) SetCloseRange();
        if (keyboard.f8Key.wasPressedThisFrame) SetMediumRange();
        if (keyboard.f9Key.wasPressedThisFrame) SetLongRange();
        if (keyboard.f10Key.wasPressedThisFrame) RespawnCurrent();
    }

    private void OnDestroy()
    {
        BindTrackedWeapon(null);
        if (observationTargetVitals != null)
            observationTargetVitals.OnHealthChanged -= OnObservationTargetHealthChanged;
    }

    public void SpawnSmily()
    {
        SpawnVariant(MonsterVariant.Smily, smilyPrefab);
    }

    public void SpawnClown()
    {
        SpawnVariant(MonsterVariant.Clown, clownPrefab);
    }

    public void SpawnOctopus()
    {
        SpawnVariant(MonsterVariant.Octopus, octopusPrefab);
    }

    public void SetLiveCombatMode()
    {
        testMode = TestMode.LiveCombat;
        ApplyTestMode(respawnCurrent: true);
    }

    public void SetSafeObservationMode()
    {
        testMode = TestMode.SafeObservation;
        ApplyTestMode(respawnCurrent: true);
    }

    public void SetCloseRange()
    {
        SetEncounterRange(EncounterRange.Close);
    }

    public void SetMediumRange()
    {
        SetEncounterRange(EncounterRange.Medium);
    }

    public void SetLongRange()
    {
        SetEncounterRange(EncounterRange.Long);
    }

    public void RespawnCurrent()
    {
        if (!lastSpawnedVariant.HasValue)
        {
            UpdateStatus("Nothing to Respawn", "Choose Smily, Clown, or Octopus first.");
            return;
        }

        MonsterVariant variant = lastSpawnedVariant.Value;
        SpawnVariant(variant, GetPrefab(variant));
    }

    private void SpawnVariant(MonsterVariant variant, GameObject prefab)
    {
        if (prefab == null)
        {
            UpdateStatus("Spawn Failed", $"{variant} prefab is not assigned.");
            return;
        }

        if (spawnRoot == null || spawnAnchor == null || analysisTarget == null)
        {
            UpdateStatus("Spawn Failed", "Monster test scene is missing spawn references.");
            return;
        }

        RebuildRegistry();
        ClearSpawnedMonsters();

        Transform encounterTarget = ResolveEncounterTarget();
        UpdateSpawnAnchor(encounterTarget);

        if (!TryResolveNavMeshSpawnPose(prefab, out Vector3 spawnPosition, out Quaternion rotation, out string failure))
        {
            UpdateStatus("Spawn Failed", failure);
            return;
        }

        GameObject instance = Instantiate(prefab, spawnPosition, rotation, spawnRoot);
        instance.name = prefab.name;
        if (!EnsureSpawnedAgentsOnNavMesh(instance, out failure))
        {
            Destroy(instance);
            UpdateStatus("Spawn Failed", failure);
            return;
        }

        TrySpawnNetworkIdentity(instance, prefab);
        DisableDormantNetworkSync(instance);
        EnsureTelemetrySupport(instance);
        activeMonster = instance;
        activeVariant = variant;
        lastSpawnedVariant = variant;
        activeDescription = GetDescription(variant);
        ResetCombatMetrics();
        nextRuntimeStatusRefreshAt = 0f;
        RefreshRuntimeStatus();
    }

    private bool TryResolveNavMeshSpawnPose(GameObject prefab, out Vector3 position, out Quaternion rotation, out string failure)
    {
        position = spawnAnchor.position;
        rotation = Quaternion.identity;
        failure = string.Empty;

        Transform encounterTarget = ResolveEncounterTarget();

        NavMeshAgent prefabAgent = prefab != null ? prefab.GetComponentInChildren<NavMeshAgent>(true) : null;
        int agentTypeId = prefabAgent != null ? prefabAgent.agentTypeID : 0;
        int areaMask = prefabAgent != null ? prefabAgent.areaMask : NavMesh.AllAreas;
        float sampleRadius = Mathf.Max(2f, prefabAgent != null ? prefabAgent.radius + 4f : 4f);

        if (!TrySampleNavMeshForAgent(spawnAnchor.position, agentTypeId, areaMask, sampleRadius, out NavMeshHit hit))
        {
            failure = $"No NavMesh point for {prefab.name} near spawn anchor. agentType={agentTypeId} radius={sampleRadius:F1}";
            return false;
        }

        position = hit.position;
        Vector3 toTarget = encounterTarget != null
            ? encounterTarget.position - position
            : analysisTarget.position - position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 0.0001f)
            rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);

        return true;
    }

    private static bool EnsureSpawnedAgentsOnNavMesh(GameObject instance, out string failure)
    {
        failure = string.Empty;
        if (instance == null)
        {
            failure = "Spawned instance is missing.";
            return false;
        }

        NavMeshAgent[] agents = instance.GetComponentsInChildren<NavMeshAgent>(true);
        for (int i = 0; i < agents.Length; i++)
        {
            NavMeshAgent agent = agents[i];
            if (agent == null || !agent.enabled)
                continue;

            float sampleRadius = Mathf.Max(2f, agent.radius + 4f);
            if (!TrySampleNavMeshForAgent(agent.transform.position, agent.agentTypeID, agent.areaMask, sampleRadius, out NavMeshHit hit))
            {
                failure = $"{instance.name} has no NavMesh point under agent {agent.name}. agentType={agent.agentTypeID}";
                return false;
            }

            if (!agent.isOnNavMesh && !agent.Warp(hit.position))
            {
                failure = $"{instance.name} failed to warp agent {agent.name} onto NavMesh at {hit.position}.";
                return false;
            }
        }

        return true;
    }

    private static bool TrySampleNavMeshForAgent(Vector3 sourcePosition, int agentTypeId, int areaMask, float maxDistance, out NavMeshHit hit)
    {
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agentTypeId,
            areaMask = areaMask
        };

        return NavMesh.SamplePosition(sourcePosition, out hit, maxDistance, filter);
    }

    private void ClearSpawnedMonsters()
    {
        if (spawnRoot == null)
            return;

        activeMonster = null;
        activeVariant = null;
        activeDescription = string.Empty;
        ResetCombatMetrics();

        for (int i = spawnRoot.childCount - 1; i >= 0; i--)
        {
            GameObject child = spawnRoot.GetChild(i).gameObject;
            if (child == null)
                continue;

            NetworkIdentity identity = child.GetComponent<NetworkIdentity>();
            if (identity != null && identity.isSpawned && NetworkManager.main != null && NetworkManager.main.isServer)
            {
                identity.Despawn();
                if (child != null)
                    child.SetActive(false);
                continue;
            }

            child.SetActive(false);
            Destroy(child);
        }
    }

    private void TrySpawnNetworkIdentity(GameObject instance, GameObject prefab)
    {
        if (instance == null || prefab == null || NetworkManager.main == null || !NetworkManager.main.isServer)
            return;

        NetworkIdentity identity = instance.GetComponent<NetworkIdentity>();
        if (identity != null && !identity.isSpawned)
            identity.Spawn(prefab);
    }

    private static void DisableDormantNetworkSync(GameObject instance)
    {
        if (instance == null)
            return;

        NetworkIdentity identity = instance.GetComponent<NetworkIdentity>();
        if (identity != null && identity.isSpawned)
            return;

        NetworkTransform networkTransform = instance.GetComponent<NetworkTransform>();
        if (networkTransform != null)
            networkTransform.enabled = false;

        NetworkAnimator networkAnimator = instance.GetComponent<NetworkAnimator>();
        if (networkAnimator != null)
            networkAnimator.enabled = false;

        Animator[] animators = instance.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            if (animators[i] != null)
                animators[i].enabled = true;
        }
    }

    private void ConfigureAnalysisTarget()
    {
        if (analysisTarget == null)
            return;

        analysisTarget.tag = "Player";

        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
            analysisTarget.gameObject.layer = playerLayer;

        if (analysisTarget.GetComponent<PlayerPawn>() == null)
            analysisTarget.gameObject.AddComponent<PlayerPawn>();

        observationTargetVitals = analysisTarget.GetComponent<PlayerVitals>();
        if (observationTargetVitals == null)
            observationTargetVitals = analysisTarget.gameObject.AddComponent<PlayerVitals>();

        observationTargetVitals.OnHealthChanged -= OnObservationTargetHealthChanged;
        observationTargetVitals.OnHealthChanged += OnObservationTargetHealthChanged;

        if (analysisTarget.GetComponent<Collider>() == null)
        {
            CapsuleCollider capsule = analysisTarget.gameObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.height = 1.8f;
            capsule.radius = 0.35f;
        }
    }

    private void ApplyTestMode(bool respawnCurrent)
    {
        if (analysisTarget != null)
        {
            bool observation = testMode == TestMode.SafeObservation;
            analysisTarget.gameObject.SetActive(observation);
            if (observation)
                RecoverObservationTarget();
        }

        UpdateChecklist();

        if (respawnCurrent && lastSpawnedVariant.HasValue)
            RespawnCurrent();
        else
            nextRuntimeStatusRefreshAt = 0f;
    }

    private void SetEncounterRange(EncounterRange range)
    {
        encounterRange = range;
        UpdateChecklist();

        if (lastSpawnedVariant.HasValue)
            RespawnCurrent();
        else
            nextRuntimeStatusRefreshAt = 0f;
    }

    private void UpdateSpawnAnchor(Transform encounterTarget)
    {
        if (spawnAnchor == null)
            return;

        Transform origin = encounterTarget != null ? encounterTarget : playerSpawnPoint;
        if (origin == null)
            return;

        Vector3 forward = origin.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        spawnAnchor.position = origin.position + forward.normalized * CurrentRangeMeters;
        spawnAnchor.rotation = Quaternion.LookRotation(-forward.normalized, Vector3.up);
    }

    private Transform ResolveEncounterTarget()
    {
        if (testMode == TestMode.SafeObservation && analysisTarget != null && analysisTarget.gameObject.activeInHierarchy)
            return analysisTarget;

        PlayerPawn pawn = ResolveLivePlayerPawn();
        if (pawn != null)
            return pawn.transform;

        return playerSpawnPoint != null ? playerSpawnPoint : analysisTarget;
    }

    private PlayerPawn ResolveLivePlayerPawn()
    {
        PlayerPawn fallback = null;
        PlayerPawn analysisPawn = analysisTarget != null ? analysisTarget.GetComponent<PlayerPawn>() : null;

        for (int i = 0; i < PlayerPawn.All.Count; i++)
        {
            PlayerPawn pawn = PlayerPawn.All[i];
            if (pawn == null || pawn == analysisPawn || !pawn.gameObject.activeInHierarchy)
                continue;

            fallback ??= pawn;
            Camera camera = pawn.GetComponentInChildren<Camera>(true);
            if (camera != null && camera.isActiveAndEnabled)
                return pawn;
        }

        return fallback;
    }

    private float CurrentRangeMeters => encounterRange switch
    {
        EncounterRange.Close => closeRangeMeters,
        EncounterRange.Long => longRangeMeters,
        _ => mediumRangeMeters
    };

    private void RebuildRegistry()
    {
        if (registryRoot != null)
            DungeonPointRegistry.RebuildFromRoot(registryRoot);
    }

    private void WireButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null)
            return;

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private void UpdateStatus(string title, string details)
    {
        if (statusLabel == null)
            return;

        string networkState = NetworkManager.main == null
            ? "no network manager"
            : NetworkManager.main.isHost ? "host"
            : NetworkManager.main.isServer ? "server"
            : NetworkManager.main.isClient ? "client"
            : "offline";

        string modeLabel = testMode == TestMode.LiveCombat ? "LIVE COMBAT" : "SAFE OBSERVATION";
        statusLabel.text = $"{title}\n{modeLabel} | {encounterRange.ToString().ToUpperInvariant()} {CurrentRangeMeters:F0}m\n{details}\nNet: {networkState}";
    }

    private void RefreshRuntimeStatus()
    {
        if (activeMonster == null)
        {
            UpdateStatus(
                "Gun vs Monster Test Ready",
                "Pick up AK / Drake-12 / RPG, then choose a monster. Range or mode changes automatically respawn the last monster."
            );
            return;
        }

        IMonsterFlowTelemetry telemetry = ResolveTelemetry(activeMonster);
        if (telemetry == null)
        {
            string variantTitle = activeVariant.HasValue ? $"{activeVariant.Value} spawned" : "Monster spawned";
            UpdateStatus(variantTitle, $"{activeDescription}\n{BuildCombatMetricsText()}");
            return;
        }

        string targetName = telemetry.HasTarget && !string.IsNullOrEmpty(telemetry.CurrentTargetName)
            ? telemetry.CurrentTargetName
            : "none";

        string attackPhase = telemetry.IsAttacking && !string.IsNullOrEmpty(telemetry.CurrentAttackPhase)
            ? telemetry.CurrentAttackPhase
            : "none";

        Vector3 destination = telemetry.DesiredDestination;
        string runtimeDetails =
            $"{activeDescription}\n" +
            $"{BuildCombatMetricsText()}\n" +
            $"Intent: {telemetry.CurrentIntent} | Target: {targetName}\n" +
            $"Traverse: {(telemetry.IsTraversing ? "yes" : "no")} | Attack: {attackPhase}\n" +
            $"Dest: ({destination.x:F1}, {destination.y:F1}, {destination.z:F1})";

        string title = activeVariant.HasValue ? $"{activeVariant.Value} spawned" : "Monster spawned";
        UpdateStatus(title, runtimeDetails);
    }

    private void ResetCombatMetrics()
    {
        encounterStartedAt = Time.time;
        metricsArmAt = Time.time + Mathf.Max(0.05f, metricsArmDelay);
        firstDamageAt = -1f;
        lastDamageAt = -1f;
        lastDamageGap = -1f;
        deathAt = -1f;
        shotsFired = 0;
        damagingEvents = 0;
        totalDamage = 0;
        lastHealth = -1;
        metricsArmed = false;
    }

    private void TrackCombatMetrics()
    {
        if (activeMonster == null || Time.time < metricsArmAt)
            return;

        if (!TryGetHealthSnapshot(out int currentHealth, out int maxHealth, out int deadUnits, out int totalUnits))
            return;

        if (!metricsArmed)
        {
            metricsArmed = true;
            lastHealth = currentHealth;
            return;
        }

        if (currentHealth < lastHealth)
        {
            int damage = lastHealth - currentHealth;
            float now = Time.time;
            totalDamage += damage;
            damagingEvents++;
            if (firstDamageAt < 0f)
                firstDamageAt = now;
            if (lastDamageAt >= 0f)
                lastDamageGap = now - lastDamageAt;
            lastDamageAt = now;
        }

        if (deathAt < 0f && totalUnits > 0 && deadUnits >= totalUnits)
            deathAt = Time.time;

        lastHealth = currentHealth;
    }

    private bool TryGetHealthSnapshot(out int currentHealth, out int maxHealth, out int deadUnits, out int totalUnits)
    {
        currentHealth = 0;
        maxHealth = 0;
        deadUnits = 0;
        totalUnits = 0;

        if (activeMonster == null)
            return false;

        MonsterHealth[] healthComponents = activeMonster.GetComponentsInChildren<MonsterHealth>(true);
        for (int i = 0; i < healthComponents.Length; i++)
        {
            MonsterHealth health = healthComponents[i];
            if (health == null)
                continue;

            totalUnits++;
            maxHealth += Mathf.Max(1, health.MaxHealth);
            currentHealth += Mathf.Max(0, health.Health);
            if (health.IsDead)
                deadUnits++;
        }

        OctopusSwarmMember[] members = activeMonster.GetComponentsInChildren<OctopusSwarmMember>(true);
        for (int i = 0; i < members.Length; i++)
        {
            OctopusSwarmMember member = members[i];
            if (member == null)
                continue;

            totalUnits++;
            maxHealth += Mathf.Max(1, member.MaxHealth);
            currentHealth += Mathf.Max(0, member.CurrentHealth);
            if (member.IsDead)
                deadUnits++;
        }

        return totalUnits > 0 && maxHealth > 0;
    }

    private string BuildCombatMetricsText()
    {
        string weaponName = trackedWeapon != null && trackedWeapon.WeaponData != null
            ? trackedWeapon.WeaponData.name
            : "none";

        if (!TryGetHealthSnapshot(out int currentHealth, out int maxHealth, out int deadUnits, out int totalUnits))
            return $"Weapon: {weaponName} | Waiting for health data...";

        float elapsed = Mathf.Max(0f, (deathAt >= 0f ? deathAt : Time.time) - encounterStartedAt);
        string firstHit = firstDamageAt >= 0f ? $"{firstDamageAt - encounterStartedAt:F2}s" : "--";
        string gap = lastDamageGap >= 0f ? $"{lastDamageGap:F2}s" : "--";
        string ttk = deathAt >= 0f ? $"{deathAt - encounterStartedAt:F2}s" : $"running {elapsed:F1}s";

        return
            $"Weapon: {weaponName} | HP {currentHealth}/{maxHealth} | Dead {deadUnits}/{totalUnits}\n" +
            $"Shots {shotsFired} | Damage events {damagingEvents} | Total damage {totalDamage}\n" +
            $"First hit {firstHit} | Last hit gap {gap} | TTK {ttk}";
    }

    private void RefreshTrackedWeapon()
    {
        if (Time.time < nextWeaponRefreshAt)
            return;

        nextWeaponRefreshAt = Time.time + 0.25f;
        PlayerPawn pawn = ResolveLivePlayerPawn();
        FPSWeaponManager manager = pawn != null ? pawn.GetComponentInChildren<FPSWeaponManager>(true) : null;
        Weapon nextWeapon = manager != null ? manager.GetActiveItem() as Weapon : null;
        BindTrackedWeapon(nextWeapon);
    }

    private void BindTrackedWeapon(Weapon weapon)
    {
        if (trackedWeapon == weapon)
            return;

        if (trackedWeapon != null)
            trackedWeapon.OnShotFired -= OnTrackedShotFired;

        trackedWeapon = weapon;

        if (trackedWeapon != null)
            trackedWeapon.OnShotFired += OnTrackedShotFired;
    }

    private void OnTrackedShotFired()
    {
        if (activeMonster != null)
            shotsFired++;
    }

    private void TickObservationTargetRecovery()
    {
        if (testMode != TestMode.SafeObservation || Time.time < nextObservationRecoveryAt)
            return;

        nextObservationRecoveryAt = Time.time + Mathf.Max(0.1f, observationTargetRecoveryInterval);
        RecoverObservationTarget();
    }

    private void RecoverObservationTarget()
    {
        if (analysisTarget == null)
            return;

        PlayerVitals vitals = analysisTarget.GetComponent<PlayerVitals>();
        if (vitals == null)
            return;

        if (vitals.IsDead)
            vitals.ReviveToFull();
        else if (vitals.CurrentHealth < vitals.MaxHealth)
            vitals.Heal(vitals.MaxHealth);
    }

    private void OnObservationTargetHealthChanged(int currentHealth, int maxHealth)
    {
        if (testMode != TestMode.SafeObservation || observationTargetVitals == null)
            return;

        if (!observationTargetVitals.IsDead && currentHealth < maxHealth)
            observationTargetVitals.Heal(maxHealth);
    }

    private void UpdateChecklist()
    {
        if (checklistLabel == null)
            return;

        checklistLabel.text =
            "F1/F2/F3 monster | F5/F6 mode | F7/F8/F9 range | F10 reset\n" +
            "JUDGE AFTER 3 REPEATS\n" +
            "1. Hit is readable without hiding the target.\n" +
            "2. Reaction does not break attack/movement unfairly.\n" +
            "3. Death timing, pose, sound and VFX agree.\n" +
            "4. Dead AI/collision stops; TTK fits ammo rhythm.";
    }

    private GameObject GetPrefab(MonsterVariant variant)
    {
        return variant switch
        {
            MonsterVariant.Smily => smilyPrefab,
            MonsterVariant.Clown => clownPrefab,
            MonsterVariant.Octopus => octopusPrefab,
            _ => null
        };
    }

    private static void EnsureTelemetrySupport(GameObject instance)
    {
        if (instance == null || ResolveTelemetry(instance) != null)
            return;

        if (instance.GetComponentInChildren<RangeDetector>(true) != null)
        {
            Debug.LogWarning(
                $"[MonsterTestSceneController] {instance.name} has no monster telemetry component. Fix the prefab or run the explicit editor setup; runtime auto-attach is disabled.",
                instance);
        }
    }

    private static IMonsterFlowTelemetry ResolveTelemetry(GameObject instance)
    {
        if (instance == null)
            return null;

        MonoBehaviour[] behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IMonsterFlowTelemetry telemetry)
                return telemetry;
        }

        return null;
    }

    private static string GetDescription(MonsterVariant variant)
    {
        return variant switch
        {
            MonsterVariant.Smily => "Single melee hunter with range detection, line of sight, jump routing, and close-range hitbox attacks.",
            MonsterVariant.Clown => "Single ranged harasser with chase logic, gift throw timing, and teleport fallback around patrol or spawn markers.",
            MonsterVariant.Octopus => "Seven-member swarm that shares a center point, encircles a target, and staggers melee attack slots.",
            _ => "Monster spawned."
        };
    }
}
