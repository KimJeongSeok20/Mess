using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class PlayerPawn : NetworkBehaviour
{
    public static readonly List<PlayerPawn> All = new(); //플레이어 리스트

    // Loot scent: value of the loot this player carries (owner reports, server clamps).
    private readonly SyncVar<int> _carriedValue = new(0, ownerAuth: false);
    public int CarriedValue => _carriedValue.value;

    /// <summary>Owner only: report the local inventory value.</summary>
    public void ReportCarriedValue(int value)
    {
        if (!isOwner)
            return;

        if (isServer)
            _carriedValue.value = Mathf.Clamp(value, 0, LootScent.MaxReportedValue);
        else
            ReportCarriedValueServerRpc(value);
    }

    [ServerRpc]
    private void ReportCarriedValueServerRpc(int value)
    {
        _carriedValue.value = Mathf.Clamp(value, 0, LootScent.MaxReportedValue);
    }

    [SerializeField] private bool applyPlayerLayerToColliders = true;

    private void Awake()
    {
        ApplyPlayerPhysicsLayer();
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) //플레이어 리스트 등록
            All.Add(this);

        ApplyPlayerPhysicsLayer();
    }

    private void OnDisable()
    {
        All.Remove(this); //플레이어 리스트 제거
    }

    private void ApplyPlayerPhysicsLayer()
    {
        if (!applyPlayerLayerToColliders)
            return;

        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer < 0)
            return;

        gameObject.layer = playerLayer;

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider != null)
                collider.gameObject.layer = playerLayer;
        }
    }
}
