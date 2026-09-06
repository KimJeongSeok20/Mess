using System.Collections;
using PurrNet;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using GoreSimulatorComponent = PampelGames.GoreSimulator.GoreSimulator;

[DisallowMultipleComponent]
public class PlayerDeath : MonoBehaviour
{
    [Header("UI (Canvas)")]
    [SerializeField] private CanvasGroup jumpscareCanvas;
    [SerializeField] private RectTransform faceImageRT;
    [SerializeField] private Image faceImage;
    [SerializeField] private Image overlayImage;
    [SerializeField] private Text deathText;

    [Header("Default Sequence")]
    [SerializeField] private float defaultMoveTime = 0.25f;
    [SerializeField] private float defaultStayTime = 0.7f;
    [SerializeField] private float defaultOverlayMaxAlpha = 0.7f;
    [SerializeField] private Color deathOverlayColor = Color.black;
    [SerializeField] private string deathMessage = "YOU DIED";
    [SerializeField] private Color deathTextColor = Color.red;
    [SerializeField, Min(1)] private int deathTextFontSize = 104;

    public enum DeathMode
    {
        /// <summary>죽으면 부활실(StartRoom)에서 유령으로 대기. 동료가 부활 장치에 돈을 내거나 다음 날이 되면 살아난다.</summary>
        WaitForRevive,
        /// <summary>respawnDelay 뒤 그 자리에서 즉시 부활 (테스트/캐주얼).</summary>
        RespawnAfterDelay,
        /// <summary>씬 리로드 (오프라인 테스트 씬 전용, 세션 중엔 무시).</summary>
        ReloadScene
    }

    [Header("After Death")]
    [Tooltip("사망 후 처리 방식. 세션 중에는 ReloadScene을 골라도 부활실 대기로 대체된다.")]
    [SerializeField] private DeathMode deathMode = DeathMode.WaitForRevive;
    [SerializeField] private bool respawnAfterSequence = false;
    [SerializeField, Min(0f)] private float respawnDelay = 0.35f;
    [SerializeField] private Transform respawnPointOverride;
    [SerializeField] private bool resetTimeOnRespawn = false;
    [SerializeField] private bool reloadSceneAfterSequence = true;
    [SerializeField, Min(0f)] private float reloadDelay = 0f;
    [SerializeField] private bool unlockCursorOnDeath = true;
    [Tooltip("Death cost: the owning client drops every carried item where it died so the team can recover it.")]
    [SerializeField] private bool dropInventoryOnDeath = true;

    [Header("Gore Death")]
    [SerializeField] private GoreSimulatorComponent playerGoreSimulator;
    [SerializeField] private float goreExplosionForce = 5f;
    [SerializeField] private float goreExplosionRadius = 1.5f;
    [SerializeField, Min(0f)] private float gorePartImpulse = 0.9f;
    [SerializeField, Min(0f)] private float gorePartTorque = 0.6f;
    [SerializeField, Min(0.1f)] private float gorePartMaxSpeed = 3f;
    [SerializeField, Min(0.1f)] private float gorePartMaxAngularSpeed = 8f;
    [SerializeField, Min(0.1f)] private float goreRemainsLifetime = 8f;
    [SerializeField] private GameObject deathVfxPrefab;
    [SerializeField, Min(0.1f)] private float deathVfxScale = 2.5f;
    [SerializeField] private Color deathVfxColor = new(0.55f, 0.015f, 0.02f, 1f);
    [SerializeField, Min(0.1f)] private float deathVfxLifetime = 3f;
    [SerializeField] private Material deathPoolMaterial;
    [SerializeField, Min(0.1f)] private float deathDecalScale = 1.5f;
    [SerializeField, Min(0.1f)] private float deathDecalLifetime = 20f;

    private bool _isDead;
    private bool _deathSequenceCompleted;
    private bool _respawnStarted;
    private Coroutine _deathPresentationRoutine;
    private PlayerInput _playerInput;
    private NetworkPlayer _networkPlayer;
    private PlayerVitals _playerVitals;
    private CharacterController _characterController;
    private Canvas _runtimeDeathCanvas;
    private readonly List<GameObject> _supplementalGoreParts = new();
    private Material _runtimeGoreCutMaterial;

    // Everything the gore death switches off, so a respawn can switch it back on without a scene reload.
    private readonly List<Renderer> _hiddenBodyRenderers = new();
    private readonly List<Collider> _disabledColliders = new();
    private readonly List<(Rigidbody body, bool wasKinematic, bool detectedCollisions)> _rigidbodyStates = new();
    private Animator _disabledAnimator;
    private Coroutine _goreCleanupRoutine;

    private enum PlayerGoreGroup
    {
        Head,
        Torso,
        LeftArm,
        RightArm,
        LeftLeg,
        RightLeg,
        Backpack
    }

    private enum GoreRendererRouting
    {
        Weighted,
        Torso,
        ArmByPosition,
        LegByPosition
    }

    private struct GoreGroupScores
    {
        private float _head;
        private float _torso;
        private float _leftArm;
        private float _rightArm;
        private float _leftLeg;
        private float _rightLeg;

        public void Add(PlayerGoreGroup group, float weight)
        {
            switch (group)
            {
                case PlayerGoreGroup.Head: _head += weight; break;
                case PlayerGoreGroup.LeftArm: _leftArm += weight; break;
                case PlayerGoreGroup.RightArm: _rightArm += weight; break;
                case PlayerGoreGroup.LeftLeg: _leftLeg += weight; break;
                case PlayerGoreGroup.RightLeg: _rightLeg += weight; break;
                default: _torso += weight; break;
            }
        }

        public PlayerGoreGroup Resolve()
        {
            PlayerGoreGroup bestGroup = PlayerGoreGroup.Torso;
            float bestScore = _torso;
            SelectIfHigher(PlayerGoreGroup.Head, _head, ref bestGroup, ref bestScore);
            SelectIfHigher(PlayerGoreGroup.LeftArm, _leftArm, ref bestGroup, ref bestScore);
            SelectIfHigher(PlayerGoreGroup.RightArm, _rightArm, ref bestGroup, ref bestScore);
            SelectIfHigher(PlayerGoreGroup.LeftLeg, _leftLeg, ref bestGroup, ref bestScore);
            SelectIfHigher(PlayerGoreGroup.RightLeg, _rightLeg, ref bestGroup, ref bestScore);

            if (bestGroup != PlayerGoreGroup.Torso)
                return bestGroup;

            PlayerGoreGroup bestLimb = PlayerGoreGroup.LeftArm;
            float bestLimbScore = _leftArm;
            SelectIfHigher(PlayerGoreGroup.RightArm, _rightArm, ref bestLimb, ref bestLimbScore);
            SelectIfHigher(PlayerGoreGroup.LeftLeg, _leftLeg, ref bestLimb, ref bestLimbScore);
            SelectIfHigher(PlayerGoreGroup.RightLeg, _rightLeg, ref bestLimb, ref bestLimbScore);
            return bestLimbScore >= 0.12f ? bestLimb : PlayerGoreGroup.Torso;
        }

        private static void SelectIfHigher(
            PlayerGoreGroup candidate,
            float score,
            ref PlayerGoreGroup bestGroup,
            ref float bestScore)
        {
            if (score <= bestScore)
                return;

            bestGroup = candidate;
            bestScore = score;
        }
    }

    private sealed class GoreGroupMeshBuilder
    {
        private readonly List<Vector3> _worldVertices = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<Vector4> _tangents = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<Material> _materials = new();
        private readonly List<List<int>> _submeshTriangles = new();
        private readonly Dictionary<long, int> _vertexLookup = new();
        private readonly Dictionary<int, int> _materialLookup = new();

        public GoreGroupMeshBuilder(PlayerGoreGroup group)
        {
            Group = group;
        }

        public PlayerGoreGroup Group { get; }
        public int TriangleCount { get; private set; }

        public void AddTriangle(
            int sourceId,
            int sourceIndexA,
            int sourceIndexB,
            int sourceIndexC,
            Material material,
            Vector3 positionA,
            Vector3 positionB,
            Vector3 positionC,
            Vector3 normalA,
            Vector3 normalB,
            Vector3 normalC,
            Vector4 tangentA,
            Vector4 tangentB,
            Vector4 tangentC,
            Vector2 uvA,
            Vector2 uvB,
            Vector2 uvC)
        {
            int materialId = material != null ? material.GetInstanceID() : 0;
            if (!_materialLookup.TryGetValue(materialId, out int submesh))
            {
                submesh = _materials.Count;
                _materials.Add(material);
                _submeshTriangles.Add(new List<int>());
                _materialLookup.Add(materialId, submesh);
            }

            int vertexA = GetOrAddVertex(sourceId, sourceIndexA, positionA, normalA, tangentA, uvA);
            int vertexB = GetOrAddVertex(sourceId, sourceIndexB, positionB, normalB, tangentB, uvB);
            int vertexC = GetOrAddVertex(sourceId, sourceIndexC, positionC, normalC, tangentC, uvC);
            _submeshTriangles[submesh].Add(vertexA);
            _submeshTriangles[submesh].Add(vertexB);
            _submeshTriangles[submesh].Add(vertexC);
            TriangleCount++;
        }

        private int GetOrAddVertex(
            int sourceId,
            int sourceIndex,
            Vector3 position,
            Vector3 normal,
            Vector4 tangent,
            Vector2 uv)
        {
            long key = ((long)sourceId << 32) | (uint)sourceIndex;
            if (_vertexLookup.TryGetValue(key, out int existingIndex))
                return existingIndex;

            int index = _worldVertices.Count;
            _vertexLookup.Add(key, index);
            _worldVertices.Add(position);
            _normals.Add(normal);
            _tangents.Add(tangent);
            _uvs.Add(uv);
            return index;
        }

        public GameObject Build(uint renderingLayerMask, int layer)
        {
            if (_worldVertices.Count == 0)
                return null;

            Bounds worldBounds = new(_worldVertices[0], Vector3.zero);
            for (int i = 1; i < _worldVertices.Count; i++)
                worldBounds.Encapsulate(_worldVertices[i]);

            Vector3 center = worldBounds.center;
            var localVertices = new List<Vector3>(_worldVertices.Count);
            foreach (Vector3 vertex in _worldVertices)
                localVertices.Add(vertex - center);

            var mesh = new Mesh
            {
                name = $"Player_GoreGroup_{Group}_RuntimeMesh",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            mesh.SetVertices(localVertices);
            mesh.SetNormals(_normals);
            mesh.SetTangents(_tangents);
            mesh.SetUVs(0, _uvs);
            mesh.subMeshCount = _submeshTriangles.Count;
            for (int i = 0; i < _submeshTriangles.Count; i++)
                mesh.SetTriangles(_submeshTriangles[i], i, false);
            mesh.RecalculateBounds();

            var fragment = new GameObject($"Player_GoreGroup_{Group}");
            fragment.layer = layer;
            fragment.transform.SetPositionAndRotation(center, Quaternion.identity);

            MeshFilter filter = fragment.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = fragment.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = _materials.ToArray();
            renderer.renderingLayerMask = renderingLayerMask;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return fragment;
        }
    }

    private void Awake()
    {
        if (GetComponent<PlayerVitals>() == null)
            gameObject.AddComponent<PlayerVitals>();

        if (GetComponent<SkillWebHealthTestBridge>() == null)
            gameObject.AddComponent<SkillWebHealthTestBridge>();

        _playerInput = GetComponent<PlayerInput>();
        _networkPlayer = GetComponent<NetworkPlayer>();
        _playerVitals = GetComponent<PlayerVitals>();
        _characterController = GetComponent<CharacterController>();
    }

    private void Start()
    {
        // Font lookup and runtime UI construction are expensive on first use. Prepare them
        // during player startup so the lethal damage frame only updates existing objects.
        Text preparedDeathText = ResolveDeathText();
        if (preparedDeathText != null)
            preparedDeathText.gameObject.SetActive(false);
    }

    public CanvasGroup JumpscareCanvas => jumpscareCanvas;
    public RectTransform FaceImageRT => faceImageRT;
    public Image FaceImage => faceImage;
    public Image OverlayImage => overlayImage;
    public Text DeathText => ResolveDeathText();
    public bool IsDead => _isDead;
    public bool IsSequenceComplete => _deathSequenceCompleted;

    public void Kill()
    {
        Kill(null);
    }

    public void Kill(IMonsterDeathSequence killerSeq)
    {
        if (_isDead)
            return;

        _isDead = true;
        _deathSequenceCompleted = false;
        _respawnStarted = false;
        _playerVitals?.ApplyExternalDeathState();
        ApplyDeathState();
        DropInventoryOnDeath();
        ExecutePlayerGoreDeath();

        // The jumpscare/"YOU DIED" overlay belongs to the player who died. A remote copy of a
        // teammate's death must only play the body/gore part, never the screen overlay.
        if (IsLocallyControlled())
            _deathPresentationRoutine = StartCoroutine(DefaultDeathSequence());
        else
            _deathPresentationRoutine = StartCoroutine(ProxyDeathSequence());
    }

    private IEnumerator ProxyDeathSequence()
    {
        yield return new WaitForSeconds(defaultMoveTime + defaultStayTime);
        CompleteDeathSequence();
    }

    private void DropInventoryOnDeath()
    {
        if (!dropInventoryOnDeath || !IsLocallyControlled())
            return;

        InventoryManager inventory = InstanceHandler.TryGetInstance(out InventoryManager found)
            ? found
            : FindFirstObjectByType<InventoryManager>();
        if (inventory == null)
            return;

        try
        {
            inventory.DropAllItemsAtDeath(transform.position);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
        }
    }

    public void CompleteDeathSequence()
    {
        if (!_isDead || _deathSequenceCompleted)
            return;

        _deathPresentationRoutine = null;
        _deathSequenceCompleted = true;

        // A scene reload inside a live PurrNet session destroys every networked object on this peer
        // and desyncs it permanently, so in a session a reload request becomes a revive wait.
        bool inNetworkSession = IsNetworkSessionActive();
        DeathMode mode = deathMode;
        if (mode == DeathMode.ReloadScene && inNetworkSession)
        {
            Debug.LogWarning("[PlayerDeath] ReloadScene death mode ignored during a network session; waiting for revive instead.", this);
            mode = DeathMode.WaitForRevive;
        }

        switch (mode)
        {
            case DeathMode.RespawnAfterDelay:
                StartCoroutine(RespawnAfterDelay());
                return;
            case DeathMode.ReloadScene:
                StartCoroutine(ReloadCurrentSceneAfterDelay());
                return;
            default:
                EnterReviveWaiting();
                return;
        }
    }

    // ───────────────────────── Revive room (StartRoom) ─────────────────────────

    private bool _waitingForRevive;

    /// <summary>True while this player is dead and parked in the revive room.</summary>
    public bool IsWaitingForRevive => _isDead && _waitingForRevive;

    /// <summary>
    /// Dead players do not respawn on their own: the ghost is moved to the always-lit StartRoom,
    /// keeps walking/looking (no weapons, no interaction except the revive station) and waits
    /// until a teammate pays the station or the day resets.
    /// </summary>
    private void EnterReviveWaiting()
    {
        if (IsLocallyControlled())
        {
            GameMenuController.CloseForPlayerStateChange();
            SkillWebTerminalInteraction.CloseForPlayerStateChange();
        }
        _waitingForRevive = true;
        ClearDeathUi();

        bool locallyControlled = IsLocallyControlled();
        Transform anchor = ResolveReviveWaitingPoint();
        if (anchor != null && locallyControlled)
        {
            MoveToRespawnPoint(anchor);

            // The booth is inside the dungeon: register the zone so ambience and the
            // day-end/run-restart return logic treat the ghost as "in the dungeon".
            if (ReviveStation.WaitingRoomPoint.HasValue && anchor == _waitingPointProxy)
            {
                DungeonZoneManager zone = FindFirstObjectByType<DungeonZoneManager>();
                if (zone != null && !zone.IsInDungeon)
                    zone.EnterDungeon(transform);
            }
        }

        // The ghost is locked in the booth: it can look around but not walk. Body stays hidden.
        if (_characterController != null)
            _characterController.enabled = true;

        if (_playerInput != null && locallyControlled)
            _playerInput.enabled = true;

        if (_networkPlayer != null)
            _networkPlayer.SetLocalControlActive(true);

        SetGhostMovementLocked(true);

        if (locallyControlled)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            PromptPresenter.ShowPrompt("You are dead. A teammate can pay at the reception window to release you.");
        }
    }

    private Demo.Scripts.Runtime.Character.FPSMovement _ghostLockedMovement;

    private void SetGhostMovementLocked(bool locked)
    {
        if (locked)
        {
            _ghostLockedMovement = GetComponentInChildren<Demo.Scripts.Runtime.Character.FPSMovement>(true);
            if (_ghostLockedMovement != null)
                _ghostLockedMovement.enabled = false;
            return;
        }

        if (_ghostLockedMovement != null)
        {
            _ghostLockedMovement.enabled = true;
            _ghostLockedMovement = null;
        }
    }

    private Transform ResolveReviveWaitingPoint()
    {
        if (respawnPointOverride != null)
            return respawnPointOverride;

        // Booth behind the reception window, whenever THIS peer has finished generating the dungeon
        // (a client that joined mid-generation would otherwise be parked in the void).
        if (ReviveStation.WaitingRoomPoint.HasValue && IsLocalDungeonReady())
        {
            _waitingPointProxy ??= new GameObject("ReviveWaitingPointProxy").transform;
            _waitingPointProxy.position = ReviveStation.WaitingRoomPoint.Value;
            _waitingPointProxy.rotation = Quaternion.identity;
            return _waitingPointProxy;
        }

        if (DungeonStartPoint.Instance != null && IsLocalDungeonReady())
            return DungeonStartPoint.Instance;

        return StartMapReturnPoint.Instance != null ? StartMapReturnPoint.Instance : ResolveRespawnPoint();
    }

    private Transform _waitingPointProxy;

    private static bool IsLocalDungeonReady()
    {
        NetworkDungeonController controller = FindFirstObjectByType<NetworkDungeonController>();
        return controller != null && controller.IsLocalDungeonReady;
    }

    /// <summary>
    /// Bring this (locally controlled) player back to life at <paramref name="position"/>,
    /// or at the default waiting/return point when null. Safe to call twice.
    /// </summary>
    public void ReviveNow(Vector3? position, Quaternion? rotation = null)
    {
        if (!_isDead)
            return;

        if (IsLocallyControlled())
        {
            GameMenuController.CloseForPlayerStateChange();
            SkillWebTerminalInteraction.CloseForPlayerStateChange();
        }
        CancelDeathPresentation();
        RestoreBodyAfterDeath();

        // A revive spot inside the dungeon is only safe once this peer has the dungeon geometry.
        if (position.HasValue && position.Value.y < -100f && !IsLocalDungeonReady())
        {
            Debug.LogWarning("[PlayerDeath] Revive point is in a dungeon this client has not generated yet; reviving at camp instead.", this);
            position = null;
        }

        if (position.HasValue)
        {
            if (_characterController != null)
                _characterController.enabled = false;

            transform.SetPositionAndRotation(position.Value, rotation ?? transform.rotation);
            Physics.SyncTransforms();

            if (_characterController != null)
                _characterController.enabled = true;

            DungeonZoneManager zone = FindFirstObjectByType<DungeonZoneManager>();
            if (zone != null && !zone.IsInDungeon && position.Value.y < -100f)
                zone.EnterDungeon(transform);
        }
        else
        {
            // No explicit spot (new day / run restart / offline): stand up at camp. The dungeon
            // that held the booth is gone or about to be, so never revive at the waiting point.
            Transform point = StartMapReturnPoint.Instance != null ? StartMapReturnPoint.Instance : ResolveRespawnPoint();
            if (point != null)
            {
                MoveToRespawnPoint(point);
                DungeonZoneManager zone = FindFirstObjectByType<DungeonZoneManager>();
                if (zone != null && zone.IsInDungeon)
                    zone.ExitDungeon(transform);
            }
        }

        // A network revive already has server-approved health (Last Stand may grant only 30%).
        // The owner mirrors that state; a host must not turn the presentation into another heal.
        if (_playerVitals != null && (!IsNetworkSessionActive() || !_playerVitals.isServer))
            _playerVitals.ReviveToFull();
        ClearDeathUi();
        SetGhostMovementLocked(false);

        _isDead = false;
        _deathSequenceCompleted = false;
        _respawnStarted = false;
        _waitingForRevive = false;

        bool locallyControlled = IsLocallyControlled();
        if (_playerInput != null && locallyControlled)
            _playerInput.enabled = true;

        if (_characterController != null)
            _characterController.enabled = true;

        if (_networkPlayer != null)
            _networkPlayer.SetLocalControlActive(true);

        if (locallyControlled)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            PromptPresenter.ShowPrompt("Revived!");
        }
    }

    /// <summary>
    /// Remote copy of a player the server just revived: restore the body without touching input
    /// or position (the NetworkTransform brings the position).
    /// </summary>
    public void ReviveFromNetwork()
    {
        if (!_isDead)
            return;

        CancelDeathPresentation();
        RestoreBodyAfterDeath();
        ClearDeathUi();
        SetGhostMovementLocked(false);

        if (_characterController != null)
            _characterController.enabled = true;

        _isDead = false;
        _deathSequenceCompleted = false;
        _respawnStarted = false;
        _waitingForRevive = false;
    }

    private void CancelDeathPresentation()
    {
        if (_deathPresentationRoutine == null)
            return;

        StopCoroutine(_deathPresentationRoutine);
        _deathPresentationRoutine = null;
    }

    private void OnEnable()
    {
        TimeManager.OnDayReset += HandleDayResetRevive;
    }

    private void OnDisable()
    {
        TimeManager.OnDayReset -= HandleDayResetRevive;
    }

    private void HandleDayResetRevive()
    {
        // A new day revives everyone for free. In a session the server flips the vitals SyncVar and
        // the owner revives through PlayerVitals; offline we do it here.
        if (!IsWaitingForRevive)
            return;

        if (IsNetworkSessionActive())
            return;

        ReviveNow(null);
    }

    private bool IsNetworkSessionActive()
    {
        return _networkPlayer != null && _networkPlayer.isSpawned;
    }

    private void ApplyDeathState()
    {
        bool shouldDisableLocalControl = IsLocallyControlled();

        if (shouldDisableLocalControl)
        {
            GameMenuController.CloseForPlayerStateChange();
            SkillWebTerminalInteraction.CloseForPlayerStateChange();
        }
        if (shouldDisableLocalControl && _networkPlayer != null)
            _networkPlayer.SetLocalControlActive(false, unlockCursorOnDeath);

        if (_playerInput != null)
            _playerInput.enabled = false;

        if (_characterController != null)
            _characterController.enabled = false;

        if (unlockCursorOnDeath && shouldDisableLocalControl)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void ExecutePlayerGoreDeath()
    {
        long phaseStart = System.Diagnostics.Stopwatch.GetTimestamp();
        SpawnPlayerDeathEffects();
        long effectsTicks = System.Diagnostics.Stopwatch.GetTimestamp() - phaseStart;

        Animator animator = GetComponentInChildren<Animator>(true);
        if (animator != null && animator.enabled)
        {
            animator.enabled = false;
            _disabledAnimator = animator;
        }

        if (playerGoreSimulator == null
            || playerGoreSimulator.smr == null
            || !playerGoreSimulator.meshCutInitialized)
        {
            Debug.LogError($"[PlayerDeath] Player death requires an initialized GoreSimulator on '{name}'.");
            return;
        }

        phaseStart = System.Diagnostics.Stopwatch.GetTimestamp();
        SkinnedMeshRenderer[] visibleBodyRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(renderer => renderer != null
                && renderer.enabled
                && renderer.gameObject.activeInHierarchy
                && renderer.sharedMesh != null)
            .ToArray();
        Bounds fullBodyBounds = CalculateRendererBounds(visibleBodyRenderers);
        Bounds goreSourceBounds = playerGoreSimulator.smr.bounds;
        uint renderingLayerMask = playerGoreSimulator.smr.renderingLayerMask;
        long discoverTicks = System.Diagnostics.Stopwatch.GetTimestamp() - phaseStart;

        phaseStart = System.Diagnostics.Stopwatch.GetTimestamp();
        List<GameObject> groupedParts = CreateGroupedBodyFragments(
            visibleBodyRenderers,
            renderingLayerMask);
        long groupedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - phaseStart;
        phaseStart = System.Diagnostics.Stopwatch.GetTimestamp();
        AddAnatomicalCutCaps(groupedParts, renderingLayerMask);
        long capTicks = System.Diagnostics.Stopwatch.GetTimestamp() - phaseStart;

        phaseStart = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (SkinnedMeshRenderer renderer in visibleBodyRenderers)
        {
            if (renderer != null && renderer != playerGoreSimulator.smr)
            {
                renderer.enabled = false;
                _hiddenBodyRenderers.Add(renderer);
            }
        }
        long hideRenderersTicks = System.Diagnostics.Stopwatch.GetTimestamp() - phaseStart;

        phaseStart = System.Diagnostics.Stopwatch.GetTimestamp();
        playerGoreSimulator.ExecuteExplosion(
            goreSourceBounds.center,
            goreExplosionForce,
            out List<GameObject> rawGoreParts);
        playerGoreSimulator.smr.enabled = false;
        _hiddenBodyRenderers.Add(playerGoreSimulator.smr);
        long simulatorTicks = System.Diagnostics.Stopwatch.GetTimestamp() - phaseStart;

        phaseStart = System.Diagnostics.Stopwatch.GetTimestamp();
        HideRawGoreSimulatorParts(rawGoreParts);
        long hideRawTicks = System.Diagnostics.Stopwatch.GetTimestamp() - phaseStart;
        phaseStart = System.Diagnostics.Stopwatch.GetTimestamp();
        DisableOriginalPlayerPhysics();
        long disablePhysicsTicks = System.Diagnostics.Stopwatch.GetTimestamp() - phaseStart;
        phaseStart = System.Diagnostics.Stopwatch.GetTimestamp();
        StabilizeExplosionParts(groupedParts, renderingLayerMask, fullBodyBounds.center);
        long stabilizeTicks = System.Diagnostics.Stopwatch.GetTimestamp() - phaseStart;

        double ticksToMilliseconds = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Debug.Log(
            $"[PlayerGorePerfPhases] effectsMs={effectsTicks * ticksToMilliseconds:F3} "
            + $"discoverMs={discoverTicks * ticksToMilliseconds:F3} "
            + $"groupedMs={groupedTicks * ticksToMilliseconds:F3} "
            + $"capsMs={capTicks * ticksToMilliseconds:F3} "
            + $"hideRenderersMs={hideRenderersTicks * ticksToMilliseconds:F3} "
            + $"simulatorMs={simulatorTicks * ticksToMilliseconds:F3} "
            + $"hideRawMs={hideRawTicks * ticksToMilliseconds:F3} "
            + $"disablePhysicsMs={disablePhysicsTicks * ticksToMilliseconds:F3} "
            + $"stabilizeMs={stabilizeTicks * ticksToMilliseconds:F3}");

        _goreCleanupRoutine = StartCoroutine(CleanupGoreAfterDelay());
    }

    private void SpawnPlayerDeathEffects()
    {
        Vector3 position = transform.position + Vector3.up;
        if (deathVfxPrefab != null)
        {
            GameObject burst = Instantiate(deathVfxPrefab, position, deathVfxPrefab.transform.rotation);
            burst.transform.localScale = deathVfxPrefab.transform.localScale * deathVfxScale * 1.5f;
            foreach (ParticleSystem particleSystem in burst.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particleSystem.main;
                main.startColor = deathVfxColor;

                ParticleSystemRenderer particleRenderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                if (particleRenderer != null)
                {
                    var properties = new MaterialPropertyBlock();
                    particleRenderer.GetPropertyBlock(properties);
                    properties.SetColor("_Color01", deathVfxColor);
                    properties.SetColor("_EmissionColor", deathVfxColor * 0.35f);
                    particleRenderer.SetPropertyBlock(properties);
                }

                particleSystem.Clear(true);
                particleSystem.Play(true);
            }
            Destroy(burst, deathVfxLifetime);
        }

        BloodPoolVisual.SpawnOnGround(
            transform.position + Vector3.up,
            transform,
            deathDecalScale,
            deathPoolMaterial,
            deathVfxColor,
            deathDecalLifetime);
    }

    private void StabilizeExplosionParts(
        List<GameObject> parts,
        uint renderingLayerMask,
        Vector3 explosionCenter)
    {
        if (parts == null || parts.Count == 0)
            return;

        var rigidbodies = new List<Rigidbody>();
        var colliders = new List<Collider>();

        foreach (GameObject part in parts)
        {
            if (part == null)
                continue;

            foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null)
                    renderer.renderingLayerMask = renderingLayerMask;
            }

            foreach (Joint joint in part.GetComponentsInChildren<Joint>(true))
                Destroy(joint);

            Rigidbody[] partBodies = part.GetComponentsInChildren<Rigidbody>(true);
            if (partBodies.Length == 0)
            {
                MeshFilter meshFilter = part.GetComponentInChildren<MeshFilter>(true);
                GameObject physicsObject = meshFilter != null ? meshFilter.gameObject : part;
                if (!physicsObject.TryGetComponent<Collider>(out _))
                {
                    if (meshFilter != null && meshFilter.sharedMesh != null)
                    {
                        BoxCollider boxCollider = physicsObject.AddComponent<BoxCollider>();
                        boxCollider.center = meshFilter.sharedMesh.bounds.center;
                        boxCollider.size = meshFilter.sharedMesh.bounds.size;
                    }
                    else
                    {
                        physicsObject.AddComponent<BoxCollider>();
                    }
                }

                partBodies = new[] { physicsObject.AddComponent<Rigidbody>() };
            }

            foreach (Rigidbody body in partBodies)
            {
                if (body == null || rigidbodies.Contains(body))
                    continue;

                body.isKinematic = false;
                body.useGravity = true;
                body.detectCollisions = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.linearDamping = 0.75f;
                body.angularDamping = 0.8f;
                body.maxLinearVelocity = gorePartMaxSpeed;
                body.maxAngularVelocity = gorePartMaxAngularSpeed;
                body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, gorePartMaxSpeed);
                body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, gorePartMaxAngularSpeed);
                Renderer massRenderer = body.GetComponentInChildren<Renderer>(true);
                if (massRenderer != null)
                    body.mass = Mathf.Clamp(massRenderer.bounds.size.magnitude * 1.35f, 0.8f, 3.2f);
                rigidbodies.Add(body);
            }

            foreach (Collider collider in part.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null && collider.enabled && !colliders.Contains(collider))
                    colliders.Add(collider);
            }
        }

        for (int i = 0; i < colliders.Count; i++)
        {
            for (int j = i + 1; j < colliders.Count; j++)
                Physics.IgnoreCollision(colliders[i], colliders[j], true);
        }

        float effectiveImpulse = Mathf.Max(gorePartImpulse, 1.8f);
        float effectiveTorque = Mathf.Max(gorePartTorque, 0.6f);
        float effectiveMaxSpeed = Mathf.Max(gorePartMaxSpeed, 3.5f);
        float effectiveMaxAngularSpeed = Mathf.Max(gorePartMaxAngularSpeed, 8f);

        for (int i = 0; i < rigidbodies.Count; i++)
        {
            Rigidbody body = rigidbodies[i];
            Vector3 sourceDirection = Vector3.ProjectOnPlane(
                body.worldCenterOfMass - explosionCenter,
                Vector3.up).normalized;
            if (sourceDirection.sqrMagnitude < 0.01f)
                sourceDirection = StableHorizontalDirection(body.name);
            Vector3 direction = (sourceDirection + Vector3.up * 0.16f).normalized;
            body.linearVelocity = new Vector3(
                body.linearVelocity.x,
                Mathf.Min(body.linearVelocity.y, 0.45f),
                body.linearVelocity.z);
            body.linearDamping = 0.42f;
            body.AddForce(direction * effectiveImpulse, ForceMode.Impulse);
            body.AddTorque(StableTorqueDirection(body.name) * effectiveTorque, ForceMode.Impulse);
            body.maxLinearVelocity = effectiveMaxSpeed;
            body.maxAngularVelocity = effectiveMaxAngularSpeed;
            body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, effectiveMaxSpeed);
            body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, effectiveMaxAngularSpeed);
        }
    }

    private List<GameObject> CreateGroupedBodyFragments(
        IEnumerable<SkinnedMeshRenderer> sourceRenderers,
        uint renderingLayerMask)
    {
        long bakeTicks = 0;
        long groupingTicks = 0;
        long buildTicks = 0;
        _supplementalGoreParts.Clear();
        var builders = new Dictionary<PlayerGoreGroup, GoreGroupMeshBuilder>
        {
            { PlayerGoreGroup.Head, new GoreGroupMeshBuilder(PlayerGoreGroup.Head) },
            { PlayerGoreGroup.Torso, new GoreGroupMeshBuilder(PlayerGoreGroup.Torso) },
            { PlayerGoreGroup.LeftArm, new GoreGroupMeshBuilder(PlayerGoreGroup.LeftArm) },
            { PlayerGoreGroup.RightArm, new GoreGroupMeshBuilder(PlayerGoreGroup.RightArm) },
            { PlayerGoreGroup.LeftLeg, new GoreGroupMeshBuilder(PlayerGoreGroup.LeftLeg) },
            { PlayerGoreGroup.RightLeg, new GoreGroupMeshBuilder(PlayerGoreGroup.RightLeg) },
            { PlayerGoreGroup.Backpack, new GoreGroupMeshBuilder(PlayerGoreGroup.Backpack) }
        };

        foreach (SkinnedMeshRenderer source in sourceRenderers)
        {
            if (source == null || source.sharedMesh == null)
                continue;

            var bakedMesh = new Mesh { name = $"{source.sharedMesh.name}_GroupedDeathBake" };
            long phaseStart = System.Diagnostics.Stopwatch.GetTimestamp();
            source.BakeMesh(bakedMesh);
            bakeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - phaseStart;
            if (bakedMesh.vertexCount > 0)
            {
                phaseStart = System.Diagnostics.Stopwatch.GetTimestamp();
                AddRendererTrianglesToGoreGroups(source, bakedMesh, builders);
                groupingTicks += System.Diagnostics.Stopwatch.GetTimestamp() - phaseStart;
            }
            Destroy(bakedMesh);
        }

        long buildStart = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (GoreGroupMeshBuilder builder in builders.Values)
        {
            if (builder.TriangleCount == 0)
                continue;

            GameObject fragment = builder.Build(renderingLayerMask, gameObject.layer);
            if (fragment != null)
                _supplementalGoreParts.Add(fragment);
        }
        buildTicks = System.Diagnostics.Stopwatch.GetTimestamp() - buildStart;

        double ticksToMilliseconds = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Debug.Log(
            $"[PlayerGorePerfBreakdown] bakeMs={bakeTicks * ticksToMilliseconds:F3} "
            + $"groupMs={groupingTicks * ticksToMilliseconds:F3} "
            + $"buildMs={buildTicks * ticksToMilliseconds:F3}");

        return new List<GameObject>(_supplementalGoreParts);
    }

    private static void AddRendererTrianglesToGoreGroups(
        SkinnedMeshRenderer source,
        Mesh bakedMesh,
        IReadOnlyDictionary<PlayerGoreGroup, GoreGroupMeshBuilder> builders)
    {
        Vector3[] vertices = bakedMesh.vertices;
        Vector3[] normals = bakedMesh.normals;
        Vector4[] tangents = bakedMesh.tangents;
        Vector2[] uvs = bakedMesh.uv;
        BoneWeight[] boneWeights = source.sharedMesh.boneWeights;
        Transform[] bones = source.bones;
        Material[] materials = source.sharedMaterials;
        Matrix4x4 localToWorld = source.transform.localToWorldMatrix;
        Matrix4x4 normalMatrix = localToWorld.inverse.transpose;
        bool hasNormals = normals.Length == vertices.Length;
        bool hasTangents = tangents.Length == vertices.Length;
        bool hasUvs = uvs.Length == vertices.Length;
        bool hasWeights = boneWeights.Length == vertices.Length;
        string lowerRendererName = source.name.ToLowerInvariant();
        PlayerGoreGroup? forcedGroup = ResolveForcedRendererGroup(lowerRendererName, true);
        GoreRendererRouting routing = ResolveRendererRouting(lowerRendererName);
        PlayerGoreGroup[] boneGroups = CreateBoneGroupLookup(bones);
        ResolveSideAnchors(
            bones,
            lowerRendererName,
            routing,
            out Vector3 leftAnchor,
            out Vector3 rightAnchor,
            out bool hasSideAnchors);

        var worldVertices = new Vector3[vertices.Length];
        var worldNormals = hasNormals ? new Vector3[vertices.Length] : null;
        var worldTangents = new Vector4[vertices.Length];
        Vector4 fallbackTangent = new(1f, 0f, 0f, 1f);
        for (int i = 0; i < vertices.Length; i++)
        {
            worldVertices[i] = localToWorld.MultiplyPoint3x4(vertices[i]);
            if (hasNormals)
                worldNormals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            worldTangents[i] = TransformTangent(hasTangents ? tangents[i] : fallbackTangent, localToWorld);
        }

        int sourceId = source.GetInstanceID();

        for (int submesh = 0; submesh < bakedMesh.subMeshCount; submesh++)
        {
            int[] triangles = bakedMesh.GetTriangles(submesh);
            Material material = materials.Length > 0
                ? materials[Mathf.Min(submesh, materials.Length - 1)]
                : null;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int indexA = triangles[i];
                int indexB = triangles[i + 1];
                int indexC = triangles[i + 2];
                if (indexA >= vertices.Length || indexB >= vertices.Length || indexC >= vertices.Length)
                    continue;

                Vector3 positionA = worldVertices[indexA];
                Vector3 positionB = worldVertices[indexB];
                Vector3 positionC = worldVertices[indexC];
                PlayerGoreGroup group;
                if (forcedGroup.HasValue)
                {
                    group = forcedGroup.Value;
                }
                else if (routing == GoreRendererRouting.Torso)
                {
                    group = PlayerGoreGroup.Torso;
                }
                else if (routing == GoreRendererRouting.ArmByPosition
                    || routing == GoreRendererRouting.LegByPosition)
                {
                    Vector3 triangleCenter = (positionA + positionB + positionC) / 3f;
                    PlayerGoreGroup leftGroup = routing == GoreRendererRouting.ArmByPosition
                        ? PlayerGoreGroup.LeftArm
                        : PlayerGoreGroup.LeftLeg;
                    PlayerGoreGroup rightGroup = routing == GoreRendererRouting.ArmByPosition
                        ? PlayerGoreGroup.RightArm
                        : PlayerGoreGroup.RightLeg;
                    group = ResolveClosestSideGroup(
                        triangleCenter,
                        leftAnchor,
                        rightAnchor,
                        hasSideAnchors,
                        leftGroup,
                        rightGroup);
                }
                else
                {
                    group = hasWeights
                        ? ResolveTriangleGoreGroup(boneWeights, boneGroups, indexA, indexB, indexC)
                        : PlayerGoreGroup.Torso;
                }

                Vector3 faceNormal = Vector3.Cross(positionB - positionA, positionC - positionA).normalized;
                Vector3 normalA = hasNormals ? worldNormals[indexA] : faceNormal;
                Vector3 normalB = hasNormals ? worldNormals[indexB] : faceNormal;
                Vector3 normalC = hasNormals ? worldNormals[indexC] : faceNormal;

                builders[group].AddTriangle(
                    sourceId,
                    indexA,
                    indexB,
                    indexC,
                    material,
                    positionA,
                    positionB,
                    positionC,
                    normalA,
                    normalB,
                    normalC,
                    worldTangents[indexA],
                    worldTangents[indexB],
                    worldTangents[indexC],
                    hasUvs ? uvs[indexA] : Vector2.zero,
                    hasUvs ? uvs[indexB] : Vector2.zero,
                    hasUvs ? uvs[indexC] : Vector2.zero);
            }
        }
    }

    private static Vector4 TransformTangent(Vector4 tangent, Matrix4x4 localToWorld)
    {
        Vector3 direction = localToWorld.MultiplyVector(new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
        return new Vector4(direction.x, direction.y, direction.z, tangent.w);
    }

    private static PlayerGoreGroup? ResolveForcedRendererGroup(string rendererName, bool alreadyLower = false)
    {
        string lower = alreadyLower ? rendererName : rendererName.ToLowerInvariant();
        if (lower.Contains("backpack"))
            return PlayerGoreGroup.Backpack;

        if (lower.Contains("head")
            || lower.Contains("hair")
            || lower.Contains("mask")
            || lower.Contains("eyes")
            || lower.Contains("teeth")
            || lower.Contains("lashes")
            || lower.Contains("caruncle"))
            return PlayerGoreGroup.Head;

        return null;
    }

    private static GoreRendererRouting ResolveRendererRouting(string lowerRendererName)
    {
        if (lowerRendererName.Contains("belts")
            || lowerRendererName.Contains("collar")
            || lowerRendererName.Contains("shirt"))
            return GoreRendererRouting.Torso;
        if (lowerRendererName.Contains("gloves") || lowerRendererName.Contains("bracers"))
            return GoreRendererRouting.ArmByPosition;
        if (lowerRendererName.Contains("pants")
            || lowerRendererName.Contains("greaves")
            || lowerRendererName.Contains("shoes"))
            return GoreRendererRouting.LegByPosition;
        return GoreRendererRouting.Weighted;
    }

    private static PlayerGoreGroup[] CreateBoneGroupLookup(Transform[] bones)
    {
        var groups = new PlayerGoreGroup[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            groups[i] = bones[i] != null ? ResolveBoneGoreGroup(bones[i].name) : PlayerGoreGroup.Torso;
        return groups;
    }

    private static PlayerGoreGroup ResolveTriangleGoreGroup(
        BoneWeight[] weights,
        PlayerGoreGroup[] boneGroups,
        int indexA,
        int indexB,
        int indexC)
    {
        var scores = new GoreGroupScores();
        AddBoneWeightScores(weights[indexA], boneGroups, ref scores);
        AddBoneWeightScores(weights[indexB], boneGroups, ref scores);
        AddBoneWeightScores(weights[indexC], boneGroups, ref scores);
        return scores.Resolve();
    }

    private static void ResolveSideAnchors(
        Transform[] bones,
        string lowerRendererName,
        GoreRendererRouting routing,
        out Vector3 leftAnchor,
        out Vector3 rightAnchor,
        out bool hasBoth)
    {
        leftAnchor = default;
        rightAnchor = default;
        hasBoth = false;
        if (routing != GoreRendererRouting.ArmByPosition && routing != GoreRendererRouting.LegByPosition)
            return;

        string stem = routing == GoreRendererRouting.ArmByPosition
            ? (lowerRendererName.Contains("bracers") ? "lowerarm" : "hand")
            : lowerRendererName.Contains("pants")
                ? "thigh"
                : lowerRendererName.Contains("greaves") ? "calf" : "foot";
        Transform left = null;
        Transform right = null;
        string leftName = stem + "_l";
        string rightName = stem + "_r";
        for (int i = 0; i < bones.Length && (left == null || right == null); i++)
        {
            Transform bone = bones[i];
            if (bone == null)
                continue;
            if (string.Equals(bone.name, leftName, System.StringComparison.OrdinalIgnoreCase))
                left = bone;
            else if (string.Equals(bone.name, rightName, System.StringComparison.OrdinalIgnoreCase))
                right = bone;
        }

        if (left == null || right == null)
            return;
        leftAnchor = left.position;
        rightAnchor = right.position;
        hasBoth = true;
    }

    private static PlayerGoreGroup ResolveClosestSideGroup(
        Vector3 worldCenter,
        Vector3 leftAnchor,
        Vector3 rightAnchor,
        bool hasBoth,
        PlayerGoreGroup leftGroup,
        PlayerGoreGroup rightGroup)
    {
        if (hasBoth)
        {
            float leftDistance = (leftAnchor - worldCenter).sqrMagnitude;
            float rightDistance = (rightAnchor - worldCenter).sqrMagnitude;
            return leftDistance <= rightDistance ? leftGroup : rightGroup;
        }

        return worldCenter.x <= 0f ? leftGroup : rightGroup;
    }

    private static void AddBoneWeightScores(
        BoneWeight weight,
        PlayerGoreGroup[] boneGroups,
        ref GoreGroupScores scores)
    {
        AddBoneWeightScore(weight.boneIndex0, weight.weight0, boneGroups, ref scores);
        AddBoneWeightScore(weight.boneIndex1, weight.weight1, boneGroups, ref scores);
        AddBoneWeightScore(weight.boneIndex2, weight.weight2, boneGroups, ref scores);
        AddBoneWeightScore(weight.boneIndex3, weight.weight3, boneGroups, ref scores);
    }

    private static void AddBoneWeightScore(
        int boneIndex,
        float weight,
        PlayerGoreGroup[] boneGroups,
        ref GoreGroupScores scores)
    {
        if (weight <= 0f || boneIndex < 0 || boneIndex >= boneGroups.Length)
            return;

        scores.Add(boneGroups[boneIndex], weight);
    }

    private static PlayerGoreGroup ResolveBoneGoreGroup(string boneName)
    {
        string lower = boneName.ToLowerInvariant();
        if (lower.Contains("head")
            || lower.Contains("neck")
            || lower.Contains("jaw")
            || lower.Contains("eye"))
            return PlayerGoreGroup.Head;

        bool left = lower.EndsWith("_l") || lower.Contains("left");
        bool right = lower.EndsWith("_r") || lower.Contains("right");
        bool arm = lower.Contains("clavicle")
            || lower.Contains("shoulder")
            || lower.Contains("upperarm")
            || lower.Contains("lowerarm")
            || lower.Contains("forearm")
            || lower.Contains("wrist")
            || lower.Contains("hand")
            || lower.Contains("finger")
            || lower.Contains("thumb")
            || lower.Contains("index")
            || lower.Contains("middle")
            || lower.Contains("ring")
            || lower.Contains("pinky");
        bool leg = lower.Contains("thigh")
            || lower.Contains("calf")
            || lower.Contains("shin")
            || lower.Contains("lowerleg")
            || lower.Contains("foot")
            || lower.Contains("toe");

        if (arm && left) return PlayerGoreGroup.LeftArm;
        if (arm && right) return PlayerGoreGroup.RightArm;
        if (leg && left) return PlayerGoreGroup.LeftLeg;
        if (leg && right) return PlayerGoreGroup.RightLeg;
        return PlayerGoreGroup.Torso;
    }

    private void AddAnatomicalCutCaps(List<GameObject> groupedParts, uint renderingLayerMask)
    {
        if (groupedParts == null || groupedParts.Count == 0)
            return;

        GameObject torso = FindGoreGroup(groupedParts, PlayerGoreGroup.Torso);
        if (torso == null)
            return;

        Transform neck = FindModelBone("neck_01") ?? FindModelBone("spine_05");
        Transform head = FindModelBone("head") ?? FindModelBone("spine_06");
        AddCutCapPair(torso, FindGoreGroup(groupedParts, PlayerGoreGroup.Head), neck, head, 0.13f, "Neck", renderingLayerMask);
        AddCutCapPair(torso, FindGoreGroup(groupedParts, PlayerGoreGroup.LeftArm), FindModelBone("upperarm_l"), FindModelBone("lowerarm_l"), 0.12f, "LeftArm", renderingLayerMask);
        AddCutCapPair(torso, FindGoreGroup(groupedParts, PlayerGoreGroup.RightArm), FindModelBone("upperarm_r"), FindModelBone("lowerarm_r"), 0.12f, "RightArm", renderingLayerMask);
        AddCutCapPair(torso, FindGoreGroup(groupedParts, PlayerGoreGroup.LeftLeg), FindModelBone("thigh_l"), FindModelBone("calf_l") ?? FindModelBone("lowerleg_l"), 0.17f, "LeftLeg", renderingLayerMask);
        AddCutCapPair(torso, FindGoreGroup(groupedParts, PlayerGoreGroup.RightLeg), FindModelBone("thigh_r"), FindModelBone("calf_r") ?? FindModelBone("lowerleg_r"), 0.17f, "RightLeg", renderingLayerMask);
    }

    private static GameObject FindGoreGroup(IEnumerable<GameObject> parts, PlayerGoreGroup group)
    {
        string expectedName = $"Player_GoreGroup_{group}";
        return parts.FirstOrDefault(part => part != null && part.name == expectedName);
    }

    private Transform FindModelBone(string boneName)
    {
        return GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate => candidate != null
                && string.Equals(candidate.name, boneName, System.StringComparison.OrdinalIgnoreCase));
    }

    private void AddCutCapPair(
        GameObject torso,
        GameObject detachedGroup,
        Transform joint,
        Transform child,
        float radius,
        string label,
        uint renderingLayerMask)
    {
        if (torso == null || detachedGroup == null || joint == null)
            return;

        Vector3 axis = child != null ? child.position - joint.position : detachedGroup.transform.position - torso.transform.position;
        if (axis.sqrMagnitude < 0.0001f)
            axis = Vector3.up;
        axis.Normalize();

        Material cutMaterial = ResolveRuntimeCutMaterial();
        float sealRadius = radius * 1.22f;
        float depth = Mathf.Clamp(sealRadius * 0.34f, 0.04f, 0.085f);

        CreateCutCap(
            torso,
            joint.position - axis * depth * 0.24f,
            axis,
            sealRadius,
            depth,
            $"Torso_{label}",
            cutMaterial,
            renderingLayerMask);
        CreateCutCap(
            detachedGroup,
            joint.position + axis * depth * 0.24f,
            -axis,
            sealRadius * 0.98f,
            depth,
            label,
            cutMaterial,
            renderingLayerMask);
    }

    private Material ResolveRuntimeCutMaterial()
    {
        if (_runtimeGoreCutMaterial != null)
            return _runtimeGoreCutMaterial;

        Material source = playerGoreSimulator != null
            ? playerGoreSimulator.cutMaterialStatic != null
                ? playerGoreSimulator.cutMaterialStatic
                : playerGoreSimulator.cutMaterial
            : null;
        if (source == null)
            return null;

        _runtimeGoreCutMaterial = new Material(source)
        {
            name = "Player_GoreCutMaterial_Runtime",
            renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 5
        };
        if (_runtimeGoreCutMaterial.HasProperty("_BaseColor"))
            _runtimeGoreCutMaterial.SetColor("_BaseColor", new Color(0.32f, 0.008f, 0.012f, 1f));
        if (_runtimeGoreCutMaterial.HasProperty("_Tiling"))
            _runtimeGoreCutMaterial.SetVector("_Tiling", new Vector4(3f, 3f, 0f, 0f));
        return _runtimeGoreCutMaterial;
    }

    private void CreateCutCap(
        GameObject parent,
        Vector3 worldPosition,
        Vector3 normal,
        float radius,
        float depth,
        string label,
        Material material,
        uint renderingLayerMask)
    {
        if (parent == null || material == null)
            return;

        var cap = new GameObject($"Player_GoreCutCap_{label}");
        cap.layer = parent.layer;
        cap.transform.SetPositionAndRotation(worldPosition, Quaternion.LookRotation(normal, Vector3.up));
        cap.transform.SetParent(parent.transform, true);
        MeshFilter filter = cap.AddComponent<MeshFilter>();
        filter.sharedMesh = CreateIrregularCutCapMesh(radius, depth, StableHash(label));
        MeshRenderer renderer = cap.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderingLayerMask = renderingLayerMask;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;
    }

    private static Mesh CreateIrregularCutCapMesh(float radius, float thickness, uint seed)
    {
        const int segments = 18;
        const int rings = 3;
        var vertices = new List<Vector3>(segments * rings + 2)
        {
            new(0f, 0f, thickness * 0.5f),
            new(0f, 0f, -thickness * 0.5f)
        };
        var uvs = new List<Vector2>(segments * 2 + 2)
        {
            new(0.5f, 0.5f),
            new(0.5f, 0.5f)
        };

        for (int ring = 0; ring < rings; ring++)
        {
            float ringScale = ring == 0 ? 1f : ring == 1 ? 0.94f : 0.78f;
            float ringDepth = Mathf.Lerp(thickness * 0.5f, -thickness * 0.5f, ring / (float)(rings - 1));
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float phase = seed * 0.0137f;
                float irregularity = 0.91f
                    + 0.055f * Mathf.Sin(angle * 3f + phase)
                    + 0.035f * Mathf.Sin(angle * 7f - phase * 0.37f);
                float x = Mathf.Cos(angle) * radius * ringScale * irregularity;
                float y = Mathf.Sin(angle) * radius * ringScale * 0.88f * irregularity;
                vertices.Add(new Vector3(x, y, ringDepth));
                uvs.Add(new Vector2(0.5f + x / (radius * 2f), 0.5f + y / (radius * 2f)));
            }
        }

        var triangles = new List<int>(segments * 18);
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int front = 2 + i;
            int frontNext = 2 + next;
            int middle = 2 + segments + i;
            int middleNext = 2 + segments + next;
            int back = 2 + segments * 2 + i;
            int backNext = 2 + segments * 2 + next;
            triangles.Add(0); triangles.Add(front); triangles.Add(frontNext);
            triangles.Add(front); triangles.Add(middle); triangles.Add(middleNext);
            triangles.Add(front); triangles.Add(middleNext); triangles.Add(frontNext);
            triangles.Add(middle); triangles.Add(back); triangles.Add(backNext);
            triangles.Add(middle); triangles.Add(backNext); triangles.Add(middleNext);
            triangles.Add(1); triangles.Add(backNext); triangles.Add(back);
        }

        var mesh = new Mesh { name = "Player_GoreCutCap_RuntimeMesh" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void HideRawGoreSimulatorParts(IEnumerable<GameObject> rawParts)
    {
        if (rawParts == null)
            return;

        foreach (GameObject rawPart in rawParts)
        {
            if (rawPart == null)
                continue;
            foreach (Renderer renderer in rawPart.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            foreach (Collider collider in rawPart.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Rigidbody body in rawPart.GetComponentsInChildren<Rigidbody>(true))
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
        }
    }

    private void DisableOriginalPlayerPhysics()
    {
        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || !collider.enabled)
                continue;

            collider.enabled = false;
            _disabledColliders.Add(collider);
        }

        foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>(true))
        {
            if (body == null)
                continue;

            _rigidbodyStates.Add((body, body.isKinematic, body.detectCollisions));
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.detectCollisions = false;
            body.isKinematic = true;
        }
    }

    /// <summary>
    /// Undoes everything <see cref="ExecutePlayerGoreDeath"/> switched off and clears the remains,
    /// so the same player object can be reused after a respawn.
    /// </summary>
    private void RestoreBodyAfterDeath()
    {
        if (_goreCleanupRoutine != null)
        {
            StopCoroutine(_goreCleanupRoutine);
            _goreCleanupRoutine = null;
        }

        if (playerGoreSimulator != null)
        {
            playerGoreSimulator.DespawnAllObjects();
            if (playerGoreSimulator.meshCutInitialized)
                playerGoreSimulator.ResetCharacter();
        }

        DestroySupplementalGoreParts();

        foreach (Renderer renderer in _hiddenBodyRenderers)
        {
            if (renderer != null)
                renderer.enabled = true;
        }
        _hiddenBodyRenderers.Clear();

        foreach (Collider collider in _disabledColliders)
        {
            if (collider != null)
                collider.enabled = true;
        }
        _disabledColliders.Clear();

        foreach ((Rigidbody body, bool wasKinematic, bool detectedCollisions) in _rigidbodyStates)
        {
            if (body == null)
                continue;

            body.isKinematic = wasKinematic;
            body.detectCollisions = detectedCollisions;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        _rigidbodyStates.Clear();

        if (_disabledAnimator != null)
        {
            _disabledAnimator.enabled = true;
            _disabledAnimator = null;
        }
    }

    private static Vector3 StableHorizontalDirection(string value)
    {
        float angle = StableHash(value) % 3600u * 0.1f * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }

    private static Vector3 StableTorqueDirection(string value)
    {
        uint hash = StableHash(value);
        float x = ((hash & 255u) / 127.5f) - 1f;
        float y = (((hash >> 8) & 255u) / 127.5f) - 1f;
        float z = (((hash >> 16) & 255u) / 127.5f) - 1f;
        Vector3 direction = new(x, y, z);
        return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.up;
    }

    private static uint StableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261u;
            if (value == null)
                return hash;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 16777619u;
            }
            return hash;
        }
    }

    private static Bounds CalculateRendererBounds(IEnumerable<Renderer> renderers)
    {
        bool hasBounds = false;
        Bounds bounds = default;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds ? bounds : new Bounds(Vector3.zero, Vector3.one);
    }

    private IEnumerator CleanupGoreAfterDelay()
    {
        yield return new WaitForSeconds(goreRemainsLifetime);
        _goreCleanupRoutine = null;
        if (playerGoreSimulator != null)
            playerGoreSimulator.DespawnDetachedObjects();

        DestroySupplementalGoreParts();
    }

    private void DestroySupplementalGoreParts()
    {
        foreach (GameObject part in _supplementalGoreParts)
        {
            if (part == null)
                continue;

            Mesh[] runtimeMeshes = part.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter != null && filter.sharedMesh != null)
                .Select(filter => filter.sharedMesh)
                .Distinct()
                .ToArray();
            Destroy(part);
            foreach (Mesh runtimeMesh in runtimeMeshes)
                Destroy(runtimeMesh);
        }
        _supplementalGoreParts.Clear();

        if (_runtimeGoreCutMaterial != null)
        {
            Destroy(_runtimeGoreCutMaterial);
            _runtimeGoreCutMaterial = null;
        }
    }

    private bool IsLocallyControlled()
    {
        if (_networkPlayer != null && _networkPlayer.isOwner)
            return true;

        Camera ownerCamera = GetComponentInChildren<Camera>(true);
        return ownerCamera != null && ownerCamera.isActiveAndEnabled;
    }

    private IEnumerator DefaultDeathSequence()
    {
        Text text = ResolveDeathText();

        if (jumpscareCanvas != null)
        {
            jumpscareCanvas.gameObject.SetActive(true);
            jumpscareCanvas.alpha = 1f;
        }

        if (overlayImage != null)
        {
            overlayImage.gameObject.SetActive(true);
            var overlayColor = deathOverlayColor;
            overlayColor.a = 0f;
            overlayImage.color = overlayColor;
            overlayImage.rectTransform.SetAsFirstSibling();
        }

        Image resolvedFaceImage = faceImage != null ? faceImage : faceImageRT != null ? faceImageRT.GetComponent<Image>() : null;
        if (resolvedFaceImage != null)
            resolvedFaceImage.enabled = false;

        if (text != null)
        {
            text.gameObject.SetActive(true);
            text.text = deathMessage;
            text.color = deathTextColor;
            text.fontSize = deathTextFontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.rectTransform.SetAsLastSibling();
        }

        Vector2 center = Vector2.zero;
        float off = 800f;
        Vector2 startPos = new Vector2(0f, off);

        RectTransform textRect = text != null ? text.rectTransform : null;
        if (textRect != null)
        {
            textRect.anchoredPosition = startPos;
            textRect.localScale = Vector3.one * 1.2f;
        }

        float elapsed = 0f;
        while (elapsed < defaultMoveTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, defaultMoveTime));
            float eased = t * t;

            if (textRect != null)
                textRect.anchoredPosition = Vector2.Lerp(startPos, center, eased);

            if (overlayImage != null)
            {
                var overlayColor = deathOverlayColor;
                overlayColor.a = Mathf.Lerp(0f, defaultOverlayMaxAlpha, eased);
                overlayImage.color = overlayColor;
            }

            yield return null;
        }

        if (textRect != null)
            textRect.anchoredPosition = center;

        yield return new WaitForSeconds(defaultStayTime);

        CompleteDeathSequence();
    }

    private Text ResolveDeathText()
    {
        if (deathText != null)
            return deathText;

        Transform parent = jumpscareCanvas != null
            ? jumpscareCanvas.transform
            : faceImageRT != null
                ? faceImageRT
                : overlayImage != null
                    ? overlayImage.rectTransform.parent
                    : null;

        if (parent == null)
            parent = CreateRuntimeDeathCanvas().transform;

        var textObject = new GameObject("Death_Text", typeof(RectTransform), typeof(Text));
        var rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;

        deathText = textObject.GetComponent<Text>();
        deathText.font = ResolveDefaultFont();
        deathText.fontStyle = FontStyle.Bold;
        deathText.horizontalOverflow = HorizontalWrapMode.Overflow;
        deathText.verticalOverflow = VerticalWrapMode.Overflow;
        deathText.supportRichText = false;
        deathText.text = deathMessage;
        deathText.color = deathTextColor;
        deathText.fontSize = deathTextFontSize;
        deathText.alignment = TextAnchor.MiddleCenter;
        deathText.raycastTarget = false;

        return deathText;
    }

    private Canvas CreateRuntimeDeathCanvas()
    {
        if (_runtimeDeathCanvas != null)
            return _runtimeDeathCanvas;

        var canvasObject = new GameObject("PlayerDeathRuntimeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _runtimeDeathCanvas = canvasObject.GetComponent<Canvas>();
        _runtimeDeathCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _runtimeDeathCanvas.sortingOrder = 2000;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        return _runtimeDeathCanvas;
    }

    private static Font ResolveDefaultFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
            return font;

        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private IEnumerator RespawnAfterDelay()
    {
        if (_respawnStarted)
            yield break;

        _respawnStarted = true;

        if (respawnDelay > 0f)
            yield return new WaitForSeconds(respawnDelay);

        RespawnNow();
    }

    private void RespawnNow()
    {
        if (resetTimeOnRespawn)
            ResetTimeToFirstDay();

        RestoreBodyAfterDeath();

        Transform point = ResolveRespawnPoint();
        if (point != null)
            MoveToRespawnPoint(point);

        _playerVitals?.ReviveToFull();
        ClearDeathUi();

        _isDead = false;
        _deathSequenceCompleted = false;
        _respawnStarted = false;

        bool locallyControlled = IsLocallyControlled();

        // Only the controlling peer may drive input again; a remote proxy must stay input-less.
        if (_playerInput != null && locallyControlled)
            _playerInput.enabled = true;

        if (_characterController != null)
            _characterController.enabled = true;

        if (_networkPlayer != null)
            _networkPlayer.SetLocalControlActive(true);

        if (locallyControlled)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void ResetTimeToFirstDay()
    {
        TimeManager timeManager = TimeManager.Active != null ? TimeManager.Active : FindFirstObjectByType<TimeManager>();
        if (timeManager == null)
        {
            var skyDirector = OccaSoftware.Altos.Runtime.AltosSkyDirector.Instance;
            if (skyDirector != null && skyDirector.skyDefinition != null)
            {
                skyDirector.skyDefinition.dayNightCycleDuration = 0f;
                skyDirector.skyDefinition.SetDayAndTime(1, 9f);
            }
            return;
        }

        // Only the server (or an offline test scene) may rewrite run state.
        if (!timeManager.isSpawned || timeManager.isServer)
            timeManager.ServerRestartFromDayOne("player respawn with time reset");
    }

    private Transform ResolveRespawnPoint()
    {
        if (respawnPointOverride != null)
            return respawnPointOverride;

        Transform namedSpawn = FindSceneTransform("PlayerSpawnPoint") ?? FindSceneTransform("SpawnPoint");
        if (namedSpawn != null)
            return namedSpawn;

        return StartMapReturnPoint.Instance;
    }

    private static Transform FindSceneTransform(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return null;

        Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || candidate.name != objectName)
                continue;

            if (!candidate.gameObject.scene.IsValid())
                continue;

            return candidate;
        }

        return null;
    }

    private void MoveToRespawnPoint(Transform point)
    {
        if (_characterController == null)
            _characterController = GetComponent<CharacterController>();

        bool controllerWasEnabled = _characterController != null && _characterController.enabled;
        if (controllerWasEnabled)
            _characterController.enabled = false;

        transform.SetPositionAndRotation(point.position, point.rotation);
        Physics.SyncTransforms();

        if (controllerWasEnabled)
            _characterController.enabled = true;
    }

    private void ClearDeathUi()
    {
        if (deathText != null)
            deathText.gameObject.SetActive(false);

        // DefaultDeathSequence hides the jumpscare face; give it back for the next death.
        Image restoredFace = faceImage != null ? faceImage : faceImageRT != null ? faceImageRT.GetComponent<Image>() : null;
        if (restoredFace != null)
            restoredFace.enabled = true;

        if (overlayImage != null)
            overlayImage.gameObject.SetActive(false);

        if (jumpscareCanvas != null)
        {
            jumpscareCanvas.alpha = 0f;
            jumpscareCanvas.gameObject.SetActive(false);
        }
    }

    private IEnumerator ReloadCurrentSceneAfterDelay()
    {
        if (reloadDelay > 0f)
            yield return new WaitForSeconds(reloadDelay);

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEngine.SceneManagement.SceneManager.LoadScene(scene.buildIndex);
    }
}

public interface IMonsterDeathSequence
{
    IEnumerator PlayDeathSequence(PlayerDeath player);
}
