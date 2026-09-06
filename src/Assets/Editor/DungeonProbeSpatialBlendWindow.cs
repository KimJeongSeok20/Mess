using UnityEditor;
using UnityEngine;

public sealed class DungeonProbeSpatialBlendWindow : EditorWindow
{
    [MenuItem("Tools/Dungeon/Dungeon Probe Spatial Blend")]
    private static void Open()
    {
        GetWindow<DungeonProbeSpatialBlendWindow>("Probe Spatial Blend");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Dungeon Probe Spatial Blend", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        DungeonTileProbeRegistry registry = ResolveRegistry();
        if (registry == null)
        {
            EditorGUILayout.HelpBox("No active DungeonTileProbeRegistry found. Enter Play Mode and generate a dungeon.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("Registry", registry.name);
        EditorGUILayout.LabelField("Tile Sets", registry.TileSetCount.ToString());
        EditorGUILayout.LabelField("Doorway Zones", registry.DoorwayBlendZoneCount.ToString());
        EditorGUILayout.LabelField("Blend Half Depth", registry.DoorwayBlendHalfDepth.ToString("0.00"));
        EditorGUILayout.LabelField("Lateral Padding", registry.DoorwayLateralPadding.ToString("0.00"));
        EditorGUILayout.Space();

        string toggleLabel = registry.SpatialBlendEnabled
            ? "Spatial Blend: ON"
            : "Spatial Blend: OFF";

        if (GUILayout.Button(toggleLabel, GUILayout.Height(28f)))
        {
            registry.SetSpatialBlendEnabled(!registry.SpatialBlendEnabled);
            Repaint();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Force Refresh Receivers"))
                registry.ForceRefreshReceivers();

            if (GUILayout.Button("Log Current Player Sample"))
                LogCurrentPlayerSample(registry);
        }
    }

    private static DungeonTileProbeRegistry ResolveRegistry()
    {
        if (DungeonTileProbeRegistry.Active != null)
            return DungeonTileProbeRegistry.Active;

        return Object.FindFirstObjectByType<DungeonTileProbeRegistry>(FindObjectsInactive.Include);
    }

    private static void LogCurrentPlayerSample(DungeonTileProbeRegistry registry)
    {
        if (registry == null)
            return;

        if (!TryFindSamplePosition(out Vector3 position, out string source))
        {
            Debug.LogWarning("[DungeonProbeSpatialBlendWindow] No player or dynamic probe receiver sample point found.");
            return;
        }

        Debug.Log($"[DungeonProbeSpatialBlendWindow] {source}: {registry.BuildSampleReport(position)}", registry);
    }

    private static bool TryFindSamplePosition(out Vector3 position, out string source)
    {
        var receivers = Object.FindObjectsByType<DungeonDynamicProbeReceiver>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < receivers.Length; i++)
        {
            DungeonDynamicProbeReceiver receiver = receivers[i];
            if (receiver == null || !receiver.enabled)
                continue;

            if (receiver.CompareTag("Player") || receiver.name.Contains("Cyber_Generic"))
            {
                position = receiver.CurrentSamplePosition;
                source = receiver.name;
                return true;
            }
        }

        for (int i = 0; i < receivers.Length; i++)
        {
            DungeonDynamicProbeReceiver receiver = receivers[i];
            if (receiver == null || !receiver.enabled)
                continue;

            position = receiver.CurrentSamplePosition;
            source = receiver.name;
            return true;
        }

        Camera camera = Camera.main;
        if (camera != null)
        {
            position = camera.transform.position;
            source = camera.name;
            return true;
        }

        position = default;
        source = string.Empty;
        return false;
    }
}
