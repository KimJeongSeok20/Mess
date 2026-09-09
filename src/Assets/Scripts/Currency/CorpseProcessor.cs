using UnityEngine;
using PurrNet;
using System.Collections;

public class CorpseProcessor : AInteractable
{
    [Header("Processed Trophy Orb")]
    [SerializeField] private SkillPointOrb processedOrbPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField, Min(0f)] private float orbEjectionSpeed = 2.5f;

    [Header("Processing Blood VFX")]
    [SerializeField] private GameObject processingBloodVfxPrefab;
    [SerializeField] private Transform bloodOutputPoint;
    [SerializeField, Min(0.01f)] private float processingBloodVfxScale = 0.65f;
    [SerializeField, Min(0.1f)] private float processingBloodVfxLifetime = 4f;
    [SerializeField, Min(0f)] private float processingBloodDelay = 1f;
    [SerializeField, Range(1, 12)] private int processingBloodBursts = 8;
    [SerializeField, Min(0.05f)] private float processingBloodBurstInterval = 0.14f;

    [Header("Processing Blood Puddle")]
    [SerializeField] private GameObject processingBloodPuddlePrefab;
    [SerializeField, Min(0.1f)] private float processingBloodPuddleScale = 1.25f;
    [SerializeField, Min(1f)] private float processingBloodPuddleLifetime = 90f;
    private GameObject _processingPuddle;

    [Header("Processing Sound")]
    [SerializeField] private AudioSource processingAudioSource;
    [SerializeField] private AudioClip processingSound;
    [SerializeField, Range(0f, 1f)] private float processingSoundVolume = 0.16f;
    [SerializeField, Min(0.1f)] private float processingSoundDuration = 2.4f;
    private Coroutine _processingSoundRoutine;
    private float _processingSoundUntil;

    // 캐시된 컴포넌트들
    private InventoryManager _inventoryManager;
    private PromptPresenter _promptPresenter;

    #region Initialization

    private void Awake()
    {
        // InventoryManager 찾기 - 여러 방법 시도
        if (!InstanceHandler.TryGetInstance(out _inventoryManager))
        {
            _inventoryManager = FindObjectOfType<InventoryManager>();
        }

        if (_inventoryManager != null)
        {
            Debug.Log("[CorpseProcessor] InventoryManager found in Awake!");
        }
        else
        {
            Debug.LogWarning("[CorpseProcessor] InventoryManager not found in Awake, will try again later");
        }

        // PromptPresenter 찾기 (옵션)
        if (_promptPresenter == null)
            _promptPresenter = FindObjectOfType<PromptPresenter>();

        // SpawnPoint 기본값 설정
        if (spawnPoint == null)
        {
            spawnPoint = transform;
        }


    }

    private void Start()
    {
        // Start에서 한 번 더 InventoryManager 찾기 시도
        if (_inventoryManager == null)
        {
            if (!InstanceHandler.TryGetInstance(out _inventoryManager))
            {
                _inventoryManager = FindObjectOfType<InventoryManager>();
            }

            if (_inventoryManager != null)
            {
                Debug.Log("[CorpseProcessor] InventoryManager found in Start!");
            }
            else
            {
                Debug.LogWarning("[CorpseProcessor] InventoryManager still not found in Start");
            }
        }
    }

    #endregion

    #region Interaction

    public override void Interact()
    {
        // InventoryManager가 없으면 다시 찾기 시도
        if (_inventoryManager == null)
        {
            if (!InstanceHandler.TryGetInstance(out _inventoryManager))
            {
                _inventoryManager = FindObjectOfType<InventoryManager>();

                if (_inventoryManager == null)
                {
                    Debug.LogError("[CorpseProcessor] InventoryManager not found after multiple attempts!");
                    return;
                }
            }
            Debug.Log("[CorpseProcessor] InventoryManager found in Interact!");
        }

        // 현재 들고 있는 아이템 확인
        var activeItemData = _inventoryManager.GetActiveItemData();

        if (!activeItemData.HasValue)
        {
            Debug.Log("[CorpseProcessor] No item in hand!");
            return;
        }

        var itemData = activeItemData.Value;

        // Corpse 아이템인지 확인
        if (!IsCorpseItem(itemData))
        {
            Debug.Log($"[CorpseProcessor] '{itemData.itemName}' is not a corpse item!");
            return;
        }

        // 서버가 실제 월드 픽업으로 발급한 시체 소유권을 검증하고 소비한다.
        ProcessCorpseServerRpc(itemData.receiptToken);
    }

    public override bool CanInteract()
    {
        return true;  // 항상 hover 효과 표시
    }

    #endregion

    #region Network Processing

    [ServerRpc(requireOwnership: false)]
    private void ProcessCorpseServerRpc(string token, RPCInfo info = default)
    {
        var player = NetworkPlayer.FindPlayer(info.sender);
        if (player == null || !player.CanUseStation(this) || !player.ServerInventory.TryGet(token, out var trophy)) return;
        if (trophy.category != (int)ItemCategory.Corpse && !IsExtraTrophyName(trophy.itemName)) return;
        string requestedCorpseName = trophy.itemName;
        int trophyRarity = trophy.rarity;

        Debug.Log($"[CorpseProcessor] Processing server-authorized corpse: {requestedCorpseName}");

        int points = ResolveTrophyPoints(requestedCorpseName);
        if (trophyRarity >= (int)ItemRarity.Rare) points *= 2;
        if (spawnPoint == null || processedOrbPrefab == null) return;

        // Create the collectible before consuming its receipt so a failed spawn cannot eat a corpse.
        var orb = SkillPointOrb.Spawn(processedOrbPrefab, spawnPoint.position, points,
            spawnPoint.forward * orbEjectionSpeed);
        if (orb == null) return;
        if (!player.ServerInventory.Consume(token, out _))
        {
            orb.Despawn();
            return;
        }

        float bloodStartsAt = Time.time + processingBloodDelay;
        orb.Landed += (position, normal) =>
        {
            if (this != null && isActiveAndEnabled)
                PlayLandedBloodObserversRpc(position, normal, Mathf.Max(0f, bloodStartsAt - Time.time));
        };
        PlayProcessingEffectsObserversRpc();
        ContractGoal? goal = ContractEvents.GoalForTrophy(requestedCorpseName);
        if (goal.HasValue)
            ContractEvents.Report(goal.Value, 1);
        player.CompleteItemUse(token, $"Processed {requestedCorpseName}");
    }

    [ObserversRpc(runLocally: true)]
    private void PlayProcessingEffectsObserversRpc()
    {
        if (processingAudioSource != null && processingSound != null)
        {
            _processingSoundUntil = Time.time + processingSoundDuration;
            if (_processingSoundRoutine == null)
                _processingSoundRoutine = StartCoroutine(PlayProcessingSound());
        }

        if (bloodOutputPoint != null)
            StartCoroutine(PlayProcessingBlood());
    }

    private IEnumerator PlayProcessingBlood()
    {
        yield return new WaitForSeconds(processingBloodDelay);
        if (bloodOutputPoint == null) yield break;

        var outputRenderer = GetComponentInChildren<Renderer>();
        uint renderingLayers = outputRenderer != null ? outputRenderer.renderingLayerMask : 1u;
        Vector3 outputPosition = bloodOutputPoint.position + bloodOutputPoint.up * 0.35f;
        for (int i = 0; i < processingBloodBursts; i++)
        {
            BloodVfxVisual.Spawn(processingBloodVfxPrefab, outputPosition,
                bloodOutputPoint.rotation, processingBloodVfxScale, processingBloodVfxLifetime,
                renderingLayers);
            if (i + 1 < processingBloodBursts)
                yield return new WaitForSeconds(processingBloodBurstInterval);
        }
    }

    [ObserversRpc(runLocally: true)]
    private void PlayLandedBloodObserversRpc(Vector3 position, Vector3 normal, float delay)
    {
        StartCoroutine(FormPuddleAtOrbLanding(position, normal, delay));
    }

    private IEnumerator FormPuddleAtOrbLanding(Vector3 position, Vector3 normal, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (spawnPoint == null) yield break;
        var outputRenderer = GetComponentInChildren<Renderer>();
        uint renderingLayers = outputRenderer != null ? outputRenderer.renderingLayerMask : 1u;
        Vector3 source = spawnPoint.position;
        float flightTime = Mathf.Max(0.25f,
            Mathf.Sqrt(2f * Mathf.Max(0.1f, source.y - position.y) / Mathf.Max(0.1f, -Physics.gravity.y)));
        // Use the server's actual contact point, including slopes, even if the orb is collected.
        StartCoroutine(SpreadProcessingPuddle(position, normal, renderingLayers, flightTime));
    }

    private IEnumerator SpreadProcessingPuddle(Vector3 position, Vector3 normal,
        uint renderingLayers, float delay)
    {
        yield return new WaitForSeconds(delay);
        var puddle = BloodPoolVisual.SpawnAtGround(position, normal,
            processingBloodPuddleScale, processingBloodPuddlePrefab, processingBloodPuddleLifetime);
        if (puddle == null) yield break;

        // Repeated processing refreshes one stain instead of stacking coplanar decals.
        bool replacingPuddle = _processingPuddle != null;
        if (replacingPuddle) Destroy(_processingPuddle);
        _processingPuddle = puddle;
        var renderers = puddle.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers) renderer.renderingLayerMask = renderingLayers;
        var properties = new MaterialPropertyBlock();
        float elapsed = 0f;
        while (puddle != null && _processingPuddle == puddle && elapsed < 0.8f)
        {
            elapsed += Time.deltaTime;
            properties.SetFloat("_PuddleSize", replacingPuddle ? 1f : Mathf.Lerp(0.15f, 1f, elapsed / 0.8f));
            foreach (var renderer in renderers) renderer.SetPropertyBlock(properties);
            yield return null;
        }
    }

    private IEnumerator PlayProcessingSound()
    {
        processingAudioSource.clip = processingSound;
        processingAudioSource.loop = true;
        processingAudioSource.volume = 0f;
        processingAudioSource.Play();
        // Rapid successful uses extend one machine voice instead of stacking loud loops.
        while (Time.time < _processingSoundUntil)
        {
            float targetVolume = processingSoundVolume * Mathf.Clamp01((_processingSoundUntil - Time.time) / 0.25f);
            processingAudioSource.volume = Mathf.MoveTowards(processingAudioSource.volume,
                targetVolume, processingSoundVolume * Time.deltaTime / 0.08f);
            yield return null;
        }
        processingAudioSource.Stop();
        processingAudioSource.volume = 0f;
        _processingSoundRoutine = null;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        _processingSoundRoutine = null;
        if (processingAudioSource != null) processingAudioSource.Stop();
        if (_processingPuddle != null) Destroy(_processingPuddle);
    }

    [System.Serializable]
    public struct TrophyReward
    {
        [Tooltip("아이템 이름에 포함되는 키워드 (대소문자 무시)")]
        public string keyword;
        [Min(0)] public int skillPoints;
    }

    [Header("=== Trophies → Skill Points ===")]
    [Tooltip("시체/전리품 이름 키워드별 스킬 포인트. 비우면 기본표: smily 1, clown 2, octopus 1, 그 외 1")]
    [SerializeField] private TrophyReward[] trophyRewards;
    [Tooltip("시체 카테고리가 아니어도 전리품으로 받아주는 아이템 이름")]
    [SerializeField] private string[] extraTrophyNames = { "Octopus" };

    private static readonly TrophyReward[] DefaultTrophyRewards =
    {
        new TrophyReward { keyword = "smily", skillPoints = 1 },
        new TrophyReward { keyword = "clown", skillPoints = 2 },
        new TrophyReward { keyword = "octopus", skillPoints = 1 },
    };

    private int ResolveTrophyPoints(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
            return 0;

        string lower = itemName.ToLowerInvariant();
        TrophyReward[] table = trophyRewards != null && trophyRewards.Length > 0 ? trophyRewards : DefaultTrophyRewards;
        for (int i = 0; i < table.Length; i++)
        {
            if (!string.IsNullOrEmpty(table[i].keyword) && lower.Contains(table[i].keyword.ToLowerInvariant()))
                return table[i].skillPoints;
        }

        return 1;
    }

    private bool IsExtraTrophyName(string itemName)
    {
        if (extraTrophyNames == null || string.IsNullOrEmpty(itemName))
            return false;

        for (int i = 0; i < extraTrophyNames.Length; i++)
        {
            if (string.Equals(extraTrophyNames[i], itemName, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    #endregion

    #region Corpse Processing Logic

    private bool IsCorpseItem(InventoryManager.InventoryItemData itemData)
    {
        if (itemData.definition != null && itemData.definition.category == ItemCategory.Corpse)
            return true;

        return IsExtraTrophyName(itemData.itemName);
    }

    #endregion

    #region Hover Prompts

    public override void OnHover()
    {
        base.OnHover();

        // 프롬프트 표시 (옵션)
        if (_promptPresenter != null)
        {
            var activeItem = _inventoryManager?.GetActiveItemData();
            if (activeItem.HasValue && IsCorpseItem(activeItem.Value))
            {
                _promptPresenter.Show("[F] Process corpse");
            }
            else
            {
                _promptPresenter.Show("Corpse items only");
            }
        }
    }

    public override void OnStopHover()
    {
        base.OnStopHover();

        // 프롬프트 숨김
        if (_promptPresenter != null)
            _promptPresenter.Hide();
    }

    #endregion
}
