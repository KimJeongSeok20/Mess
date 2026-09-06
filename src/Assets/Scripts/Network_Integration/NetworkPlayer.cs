using PurrNet;
using UnityEngine;

public partial class NetworkPlayer : NetworkBehaviour
{
    public static event System.Action<Camera> OnLocalPlayerSpawned;
    public static event System.Action OnLocalPlayerDespawned;

    [SerializeField] private GameObject cameraRig;
    [SerializeField] private Behaviour[] ownerOnlyBehaviours;

    protected override void OnSpawned()
    {
        TimeManager.OnRunRestarted += ResetInventoryOnRunRestart;
        ApplyOwnerState(isOwner, isOwner ? CursorLockMode.Locked : CursorLockMode.None, !isOwner);

        if (isOwner)
        {
            // Ownership can arrive a tick after Start() on clients; make sure the local-player
            // statics point at this player regardless of that timing (audit bug H4).
            var movement = GetComponentInChildren<Demo.Scripts.Runtime.Character.FPSMovement>(true);
            if (movement != null)
                Demo.Scripts.Runtime.Character.FPSMovement.LocalPlayerPosition = movement;

            var controller = GetComponentInChildren<Demo.Scripts.Runtime.Character.FPSController>(true);
            if (controller != null)
                Demo.Scripts.Runtime.Character.FPSController._fpsconnect = controller;
        }

        if (isOwner && cameraRig != null)
        {
            Camera localCamera = cameraRig.GetComponentInChildren<Camera>();
            if (localCamera != null)
            {
                OnLocalPlayerSpawned?.Invoke(localCamera);
            }
            else
            {
                Debug.LogWarning("NetworkPlayer: cameraRig에서 카메라를 찾을 수 없습니다!");
            }
        }
    }

    protected override void OnDespawned()
    {
        TimeManager.OnRunRestarted -= ResetInventoryOnRunRestart;
        ResetInventoryOnRunRestart("disconnect");
        if (!isOwner)
            return;

        OnLocalPlayerDespawned?.Invoke();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void SetLocalControlActive(bool active, bool unlockCursor = false)
    {
        if (!isOwner)
            return;

        if (ownerOnlyBehaviours != null)
        {
            for (int i = 0; i < ownerOnlyBehaviours.Length; i++)
            {
                Behaviour behaviour = ownerOnlyBehaviours[i];
                if (behaviour != null)
                    behaviour.enabled = active;
            }
        }

        Cursor.lockState = unlockCursor || !active ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = unlockCursor || !active;
    }

    private void ApplyOwnerState(bool active, CursorLockMode lockMode, bool cursorVisible)
    {
        if (cameraRig != null)
            cameraRig.SetActive(active);

        if (ownerOnlyBehaviours != null)
        {
            for (int i = 0; i < ownerOnlyBehaviours.Length; i++)
            {
                Behaviour behaviour = ownerOnlyBehaviours[i];
                if (behaviour != null)
                    behaviour.enabled = active;
            }
        }

        Cursor.lockState = lockMode;
        Cursor.visible = cursorVisible;
    }

    // ───────────────────────── Revive station ─────────────────────────

    /// <summary>
    /// Owner → server: pay the team's shared currency to revive <paramref name="target"/> at
    /// <paramref name="revivePosition"/> (the revive station in the StartRoom).
    /// </summary>
    [ServerRpc]
    public void RequestReviveServerRpc(PlayerID target, Vector3 revivePosition, RPCInfo info = default)
    {
        var station = ReviveStation.Active;
        if (station == null || target == info.sender || !CanUseStation(station))
        {
            ShowMessageTargetRpc(info.sender, "A living teammate must use the reception window.");
            return;
        }
        // The supplied position is retained for RPC compatibility, but never trusted.
        revivePosition = station.ServerRevivePosition;
        PlayerVitals targetVitals = FindVitalsForPlayer(target);
        if (targetVitals == null)
        {
            ShowMessageTargetRpc(info.sender, "No player to revive.");
            return;
        }

        if (!targetVitals.IsDead)
        {
            ShowMessageTargetRpc(info.sender, "That player is already alive.");
            return;
        }

        int cost = ReviveLedger.CurrentCost;
        PlayerVitals payerVitals = FindVitalsForPlayer(info.sender);
        if (payerVitals != null && payerVitals.ServerHagglerDiscount > 0f)
            cost = Mathf.RoundToInt(cost * (1f - payerVitals.ServerHagglerDiscount));

        CurrencyManager currency = InstanceHandler.TryGetInstance(out CurrencyManager found)
            ? found
            : FindFirstObjectByType<CurrencyManager>();

        if (cost > 0)
        {
            if (currency == null)
            {
                ShowMessageTargetRpc(info.sender, "Shared currency unavailable.");
                return;
            }

            if (!currency.TrySpendCurrencyImmediateOnServer(cost))
            {
                ShowMessageTargetRpc(info.sender, $"Revive costs ${cost:N0}. Team funds: ${currency.SharedCurrency:N0}.");
                return;
            }
        }

        ReviveLedger.RegisterRevive();

        // Server copy first (replicates dead=false to everyone), then tell the owner where to stand up.
        targetVitals.ReviveToFull();
        NetworkPlayer targetPlayer = targetVitals.GetComponent<NetworkPlayer>();
        if (targetPlayer != null)
            targetPlayer.ReviveAtTargetRpc(target, revivePosition);

        ShowMessageTargetRpc(info.sender, $"Revived for ${cost:N0}. Next revive: ${ReviveLedger.CurrentCost:N0}.");
        Debug.Log($"[NetworkPlayer] {info.sender} paid ${cost} to revive {target}.");
    }

    /// <summary>Server helper for other systems (Last Stand) to stand the owner up at a position.</summary>
    public void ReviveAtOwnerTarget(PlayerID target, Vector3 position)
    {
        if (isServer)
            ReviveAtTargetRpc(target, position);
    }

    [TargetRpc]
    private void ReviveAtTargetRpc(PlayerID target, Vector3 position)
    {
        PlayerDeath death = GetComponent<PlayerDeath>();
        if (death != null)
            death.ReviveNow(position);
    }

    [TargetRpc]
    private void ShowMessageTargetRpc(PlayerID target, string message)
    {
        PromptPresenter.ShowPrompt(message);
    }

    private static PlayerVitals FindVitalsForPlayer(PlayerID player)
    {
        PlayerVitals[] all = FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            PlayerVitals vitals = all[i];
            if (vitals != null && vitals.isSpawned && vitals.owner.HasValue && vitals.owner.Value == player)
                return vitals;
        }

        return null;
    }

}
