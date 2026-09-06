using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MonsterDoorTraversalGroup : MonoBehaviour
{
    private readonly Dictionary<MonsterDoorLinkBinding, DoorClaim> _claims = new();

    public bool TryClaim(MonsterDoorLinkBinding binding, DoorAutoOpener requester, bool canOpen)
    {
        if (binding == null || requester == null || binding.IsOpen)
            return false;

        if (!canOpen)
            return false;

        if (_claims.TryGetValue(binding, out DoorClaim claim))
        {
            if (claim.Owner == null || !claim.Owner.isActiveAndEnabled)
            {
                _claims.Remove(binding);
            }
            else
            {
                return claim.Owner == requester;
            }
        }

        _claims[binding] = new DoorClaim(requester);
        return true;
    }

    public void Release(MonsterDoorLinkBinding binding, DoorAutoOpener requester)
    {
        if (binding == null || requester == null)
            return;

        if (_claims.TryGetValue(binding, out DoorClaim claim) && claim.Owner == requester)
            _claims.Remove(binding);
    }

    private void LateUpdate()
    {
        if (_claims.Count == 0)
            return;

        s_RemoveBuffer.Clear();
        foreach (KeyValuePair<MonsterDoorLinkBinding, DoorClaim> pair in _claims)
        {
            if (pair.Key == null || pair.Key.IsOpen || pair.Value.Owner == null || !pair.Value.Owner.isActiveAndEnabled)
                s_RemoveBuffer.Add(pair.Key);
        }

        for (int i = 0; i < s_RemoveBuffer.Count; i++)
            _claims.Remove(s_RemoveBuffer[i]);
    }

    private readonly struct DoorClaim
    {
        public DoorClaim(DoorAutoOpener owner)
        {
            Owner = owner;
        }

        public DoorAutoOpener Owner { get; }
    }

    private static readonly List<MonsterDoorLinkBinding> s_RemoveBuffer = new();
}
