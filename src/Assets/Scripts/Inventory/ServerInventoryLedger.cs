using System;
using System.Collections.Generic;

/// <summary>Server-owned receipts, one per physical item. Never populated from client item data.</summary>
[Serializable]
public struct InventoryReceipt
{
    public string token, itemName, weaponDataName;
    public int price, rarity, upgradeTier, ammo, category;
}

public sealed class ServerInventoryLedger
{
    private readonly Dictionary<string, InventoryReceipt> _held = new();
    public int Count => _held.Count;
    public InventoryReceipt[] Snapshot()
    {
        var entries = new InventoryReceipt[_held.Count];
        _held.Values.CopyTo(entries, 0);
        return entries;
    }

    public bool Grant(InventoryReceipt item)
    {
        if (string.IsNullOrEmpty(item.token) || string.IsNullOrEmpty(item.itemName) || _held.ContainsKey(item.token))
            return false;
        _held.Add(item.token, item);
        return true;
    }

    public bool TryGet(string token, out InventoryReceipt item)
    {
        item = default;
        return !string.IsNullOrEmpty(token) && _held.TryGetValue(token, out item);
    }

    public bool Consume(string token, out InventoryReceipt item)
    {
        if (!TryGet(token, out item)) return false;
        return _held.Remove(token);
    }

    // Rotate the main receipt even on a failed roll, so replaying that roll cannot charge twice.
    public bool Replace(string mainToken, string materialToken, InventoryReceipt replacement)
    {
        if (!TryGet(mainToken, out _) || string.IsNullOrEmpty(replacement.token)
            || _held.ContainsKey(replacement.token)) return false;
        if (!string.IsNullOrEmpty(materialToken)
            && (materialToken == mainToken || !TryGet(materialToken, out _))) return false;
        _held.Remove(mainToken);
        if (!string.IsNullOrEmpty(materialToken)) _held.Remove(materialToken);
        _held.Add(replacement.token, replacement);
        return true;
    }

    public void Clear() => _held.Clear();

    public bool ReduceAmmo(string token, int remaining)
    {
        if (!TryGet(token, out var item) || remaining < 0 || remaining > item.ammo) return false;
        item.ammo = remaining;
        _held[token] = item;
        return true;
    }

    public bool Reload(string weaponToken, string ammoToken, string ammoName, int magazineSize)
    {
        if (weaponToken == ammoToken || magazineSize <= 0 || !TryGet(weaponToken, out var weapon)
            || !TryGet(ammoToken, out var ammo) || ammo.itemName != ammoName || weapon.ammo >= magazineSize) return false;
        _held.Remove(ammoToken);
        weapon.ammo = magazineSize;
        _held[weaponToken] = weapon;
        return true;
    }
}
