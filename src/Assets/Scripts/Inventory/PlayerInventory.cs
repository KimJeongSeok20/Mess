using PurrNet;
using UnityEngine;
using UnityEngine.Rendering;

public class PlayerInventory : NetworkBehaviour
{
    public static PlayerInventory localInventory;

    protected override void OnSpawned()
    {
        base.OnSpawned();

        if (!isOwner)
            return;

        localInventory = this;
    }

    protected override void OnDespawned()
    {
        base.OnDespawned();

        if(!isOwner) 
            return;

        localInventory = null;
    }

    public void EquipItem(Item item)
    {
        if (!item)
            return;
    }
    public void UnequipItem(Item item)
    {
        if (!item)
            return;

        Debug.Log($"you called unequip method");
    }
}
