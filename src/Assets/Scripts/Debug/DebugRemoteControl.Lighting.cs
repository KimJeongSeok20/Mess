// DebugRemoteControl.Lighting.cs - dungeon probe lighting commands for unity-cli exec
//
// Usage examples:
//   unity-cli exec "return DebugRemoteControl.PortalSH(true);"
//   unity-cli exec "return DebugRemoteControl.PortalSHReport();"

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Demo.Scripts.Runtime.Character;

public partial class DebugRemoteControl
{
    // -------------------- Dungeon lighting --------------------

    /// <summary>Turn incoming door-spill SH on or off for every dynamic probe receiver (player, monsters, items).</summary>
    public static string PortalSH(bool enable = true)
    {
        var registry = FindProbeRegistry();
        if (registry == null) return "ERROR: DungeonTileProbeRegistry not found";

        registry.SetDynamicReceiversReceivePortalDirectSH(enable);
        return $"Portal direct SH for dynamic receivers -> {(enable ? "ON" : "OFF")}";
    }

    /// <summary>List dynamic probe receivers around the local player with their portal SH state.</summary>
    public static string PortalSHReport(float radius = 40f, int maxResults = 24)
    {
        var registry = FindProbeRegistry();
        var playerRoot = FindLocalPlayerRoot();
        Vector3 origin = playerRoot != null ? playerRoot.position : Vector3.zero;
        var receivers = FindObjectsByType<DungeonDynamicProbeReceiver>(FindObjectsSortMode.None);

        int enabledCount = 0;
        int withPortal = 0;
        var nearby = new List<DungeonDynamicProbeReceiver>();
        float maxDistance = Mathf.Max(0f, radius);

        for (int i = 0; i < receivers.Length; i++)
        {
            var receiver = receivers[i];
            if (receiver == null || !receiver.enabled)
                continue;

            enabledCount++;
            if (receiver.LastSampleInfo.portalContributionCount > 0)
                withPortal++;

            if (Vector3.Distance(origin, receiver.CurrentSamplePosition) <= maxDistance)
                nearby.Add(receiver);
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== Portal SH Receivers ===");
        if (registry == null)
        {
            sb.AppendLine("Registry:    none (generate the dungeon first)");
        }
        else
        {
            sb.AppendLine($"Registry:    {registry.name} active={(DungeonTileProbeRegistry.Active == registry)}");
            sb.AppendLine($"PortalSH:    {(registry.DynamicReceiversReceivePortalDirectSH ? "ON" : "OFF")} (player, monsters, items)");
            sb.AppendLine($"Visibility:  {(registry.VisibilityFilteringEnabled ? "ON" : "OFF")}  SpatialBlend: {(registry.SpatialBlendEnabled ? "ON" : "OFF")}");
            sb.AppendLine($"Tiles/Zones: {registry.TileSetCount} / {registry.DoorwayBlendZoneCount}");
        }
        sb.AppendLine($"Receivers:   {receivers.Length} (enabled={enabledCount}, receiving portal={withPortal})");

        nearby.Sort((a, b) => Vector3.Distance(origin, a.CurrentSamplePosition).CompareTo(Vector3.Distance(origin, b.CurrentSamplePosition)));
        sb.AppendLine($"Nearby Receivers ({nearby.Count}):");

        for (int i = 0; i < nearby.Count && i < maxResults; i++)
        {
            var receiver = nearby[i];
            var info = receiver.LastSampleInfo;
            float distance = Vector3.Distance(origin, receiver.CurrentSamplePosition);
            string optIn = receiver.ReceivePortalDirectSH ? "own" : receiver.EffectiveReceivePortalDirectSH ? "registry" : "off";
            sb.AppendLine(
                $"  {distance:F1}m {DescribeReceiverKind(receiver)} {receiver.name} optIn={optIn} valid={receiver.HasValidSample} " +
                $"tile={info.tileName} portal={info.portalContributionCount}/{info.portalConnectionCount} " +
                $"portalL0={info.portalL0Luminance:F3} l0={info.l0Luminance:F3} blend={info.spatialBlendActive}");
        }

        return sb.ToString();
    }

    private static DungeonTileProbeRegistry FindProbeRegistry()
    {
        if (DungeonTileProbeRegistry.Active != null)
            return DungeonTileProbeRegistry.Active;

        return FindFirstObjectByType<DungeonTileProbeRegistry>(FindObjectsInactive.Include);
    }

    private static string DescribeReceiverKind(DungeonDynamicProbeReceiver receiver)
    {
        if (receiver.GetComponentInParent<FPSController>() != null)
            return "Player";
        if (receiver.GetComponentInParent<MonsterHealth>() != null || receiver.GetComponentInParent<OctopusSwarmMember>() != null)
            return "Monster";
        if (receiver.GetComponentInParent<Item>() != null)
            return "Item";
        return "Other";
    }
}
