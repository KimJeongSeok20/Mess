using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.AI;
using UnityEngine;
using UnityEngine.AI;

public sealed class RuntimeNavMeshAgentTypeDebugWindow : EditorWindow
{
    private const int DefaultItemAgentTypeId = 0;

    private readonly Dictionary<int, bool> originalEnabledStates = new Dictionary<int, bool>();
    private readonly List<NavMeshSurface> surfaces = new List<NavMeshSurface>();
    private readonly List<AgentSurfaceGroup> groups = new List<AgentSurfaceGroup>();

    private Vector2 scroll;
    private bool onlySurfacesWithData = true;
    private bool showSurfaceDetails;
    private bool autoRefresh = true;
    private double nextRefreshTime;
    private int itemAgentTypeId = DefaultItemAgentTypeId;

    public static void Open()
    {
        RuntimeNavMeshAgentTypeDebugWindow window = GetWindow<RuntimeNavMeshAgentTypeDebugWindow>();
        window.titleContent = new GUIContent("Runtime NavMesh");
        window.minSize = new Vector2(430f, 280f);
        window.RefreshSurfaces();
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        RefreshItemAgentTypeId();
        RefreshSurfaces();
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }

    private void Update()
    {
        if (!autoRefresh || EditorApplication.timeSinceStartup < nextRefreshTime)
            return;

        nextRefreshTime = EditorApplication.timeSinceStartup + 1.0d;
        RefreshSurfaces();
        Repaint();
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawHelp();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (groups.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No runtime NavMeshSurface with NavMeshData was found. Enter Play Mode and generate the dungeon, then press Refresh.",
                MessageType.Info);
        }
        else
        {
            for (int i = 0; i < groups.Count; i++)
                DrawGroup(groups[i]);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                RefreshSurfaces();

            if (GUILayout.Button("Enable NavMesh View", EditorStyles.toolbarButton, GUILayout.Width(135f)))
                EnableUnityNavMeshView();

            GUILayout.FlexibleSpace();

            autoRefresh = GUILayout.Toggle(autoRefresh, "Auto", EditorStyles.toolbarButton, GUILayout.Width(50f));
            onlySurfacesWithData = GUILayout.Toggle(onlySurfacesWithData, "Data Only", EditorStyles.toolbarButton, GUILayout.Width(75f));
            showSurfaceDetails = GUILayout.Toggle(showSurfaceDetails, "Details", EditorStyles.toolbarButton, GUILayout.Width(65f));
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Show All"))
                SetAllVisible(true);

            if (GUILayout.Button("Hide All"))
                SetAllVisible(false);

            if (GUILayout.Button("Restore Snapshot"))
                RestoreOriginalStates();

            if (GUILayout.Button("Select Visible"))
                SelectVisibleSurfaces();
        }
    }

    private void DrawHelp()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "This tool is intended for Play Mode after runtime dungeon generation. The checkboxes toggle NavMeshSurface.enabled, so use it as a visual debug switch and restore when done.",
                MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Check an agent type to show its baked runtime surface in the Scene view. Uncheck it to hide/unload that agent type's surface. Use Unity's AI Navigation overlay with Show NavMesh enabled.",
                MessageType.None);
        }
    }

    private void DrawGroup(AgentSurfaceGroup group)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = group.EnabledCount > 0 && group.EnabledCount < group.Surfaces.Count;
                bool visible = EditorGUILayout.ToggleLeft(GetGroupDisplayName(group.AgentTypeId), group.EnabledCount > 0, EditorStyles.boldLabel);
                EditorGUI.showMixedValue = false;

                if (EditorGUI.EndChangeCheck())
                    SetAgentTypeVisible(group.AgentTypeId, visible);

                GUILayout.FlexibleSpace();

                GUILayout.Label($"{group.EnabledCount}/{group.Surfaces.Count}", GUILayout.Width(44f));

                if (GUILayout.Button("Solo", GUILayout.Width(48f)))
                    SoloAgentType(group.AgentTypeId);

                if (GUILayout.Button("Select", GUILayout.Width(58f)))
                    SelectGroup(group);
            }

            EditorGUILayout.LabelField($"agentTypeID: {group.AgentTypeId}", EditorStyles.miniLabel);

            if (!showSurfaceDetails)
                return;

            EditorGUI.indentLevel++;
            for (int i = 0; i < group.Surfaces.Count; i++)
            {
                NavMeshSurface surface = group.Surfaces[i];
                if (surface == null)
                    continue;

                string dataState = surface.navMeshData == null ? "no data" : "has data";
                string enabledState = surface.enabled && surface.gameObject.activeInHierarchy ? "visible" : "hidden";
                EditorGUILayout.ObjectField($"{enabledState}, {dataState}", surface, typeof(NavMeshSurface), true);
                EditorGUILayout.LabelField(GetHierarchyPath(surface.transform), EditorStyles.miniLabel);
            }
            EditorGUI.indentLevel--;
        }
    }

    private void RefreshSurfaces()
    {
        surfaces.Clear();
        groups.Clear();
        RefreshItemAgentTypeId();

        NavMeshSurface[] found = Resources.FindObjectsOfTypeAll<NavMeshSurface>();
        for (int i = 0; i < found.Length; i++)
        {
            NavMeshSurface surface = found[i];
            if (!IsSceneSurface(surface))
                continue;

            if (onlySurfacesWithData && surface.navMeshData == null)
                continue;

            surfaces.Add(surface);

            int instanceId = surface.GetInstanceID();
            if (!originalEnabledStates.ContainsKey(instanceId))
                originalEnabledStates.Add(instanceId, surface.enabled);
        }

        surfaces.Sort(CompareSurface);
        BuildGroups();
    }

    private void BuildGroups()
    {
        for (int i = 0; i < surfaces.Count; i++)
        {
            NavMeshSurface surface = surfaces[i];
            AgentSurfaceGroup group = FindGroup(surface.agentTypeID);
            if (group == null)
            {
                group = new AgentSurfaceGroup(surface.agentTypeID);
                groups.Add(group);
            }

            group.Surfaces.Add(surface);
            if (surface.enabled && surface.gameObject.activeInHierarchy)
                group.EnabledCount++;
        }

        groups.Sort((left, right) => string.Compare(GetGroupDisplayName(left.AgentTypeId), GetGroupDisplayName(right.AgentTypeId), StringComparison.Ordinal));
    }

    private AgentSurfaceGroup FindGroup(int agentTypeId)
    {
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i].AgentTypeId == agentTypeId)
                return groups[i];
        }

        return null;
    }

    private void SetAllVisible(bool visible)
    {
        for (int i = 0; i < groups.Count; i++)
            SetAgentTypeVisible(groups[i].AgentTypeId, visible, false);

        RefreshSurfaces();
        SceneView.RepaintAll();
    }

    private void SetAgentTypeVisible(int agentTypeId, bool visible, bool refresh = true)
    {
        for (int i = 0; i < surfaces.Count; i++)
        {
            NavMeshSurface surface = surfaces[i];
            if (surface != null && surface.agentTypeID == agentTypeId)
                SetSurfaceVisible(surface, visible);
        }

        if (!refresh)
            return;

        RefreshSurfaces();
        SceneView.RepaintAll();
    }

    private void SoloAgentType(int agentTypeId)
    {
        for (int i = 0; i < surfaces.Count; i++)
        {
            NavMeshSurface surface = surfaces[i];
            if (surface != null)
                SetSurfaceVisible(surface, surface.agentTypeID == agentTypeId);
        }

        RefreshSurfaces();
        SelectGroup(FindGroup(agentTypeId));
        SceneView.RepaintAll();
    }

    private void RestoreOriginalStates()
    {
        for (int i = 0; i < surfaces.Count; i++)
        {
            NavMeshSurface surface = surfaces[i];
            if (surface == null)
                continue;

            if (originalEnabledStates.TryGetValue(surface.GetInstanceID(), out bool originalEnabled))
                SetSurfaceVisible(surface, originalEnabled);
        }

        RefreshSurfaces();
        SceneView.RepaintAll();
    }

    private static void SetSurfaceVisible(NavMeshSurface surface, bool visible)
    {
        if (surface == null || surface.enabled == visible)
            return;

        Undo.RecordObject(surface, "Toggle runtime NavMesh surface visibility");
        surface.enabled = visible;
        EditorUtility.SetDirty(surface);
    }

    private void SelectVisibleSurfaces()
    {
        List<UnityEngine.Object> selected = new List<UnityEngine.Object>();
        for (int i = 0; i < surfaces.Count; i++)
        {
            NavMeshSurface surface = surfaces[i];
            if (surface != null && surface.enabled && surface.gameObject.activeInHierarchy)
                selected.Add(surface.gameObject);
        }

        Selection.objects = selected.ToArray();
        SetNavMeshVisualizationBool("showOnlySelectedSurfaces", selected.Count > 0);
        SceneView.FrameLastActiveSceneView();
        SceneView.RepaintAll();
    }

    private static void SelectGroup(AgentSurfaceGroup group)
    {
        if (group == null)
            return;

        List<UnityEngine.Object> selected = new List<UnityEngine.Object>();
        for (int i = 0; i < group.Surfaces.Count; i++)
        {
            NavMeshSurface surface = group.Surfaces[i];
            if (surface != null)
                selected.Add(surface.gameObject);
        }

        Selection.objects = selected.ToArray();
        SetNavMeshVisualizationBool("showOnlySelectedSurfaces", selected.Count > 0);
        SceneView.FrameLastActiveSceneView();
        SceneView.RepaintAll();
    }

    private static void EnableUnityNavMeshView()
    {
        SetNavMeshVisualizationBool("showNavMesh", true);
        SetNavMeshVisualizationBool("showHeightMesh", false);
        SceneView.RepaintAll();
    }

    private static void SetNavMeshVisualizationBool(string propertyName, bool value)
    {
        PropertyInfo property = typeof(NavMeshVisualizationSettings).GetProperty(
            propertyName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        if (property != null && property.PropertyType == typeof(bool) && property.CanWrite)
            property.SetValue(null, value);
    }

    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.EnteredEditMode)
        {
            originalEnabledStates.Clear();
            RefreshSurfaces();
            Repaint();
        }
    }

    private static bool IsSceneSurface(NavMeshSurface surface)
    {
        if (surface == null || surface.gameObject == null)
            return false;

        if (EditorUtility.IsPersistent(surface))
            return false;

        return surface.gameObject.scene.IsValid() && surface.gameObject.scene.isLoaded;
    }

    private static int CompareSurface(NavMeshSurface left, NavMeshSurface right)
    {
        int agentCompare = string.Compare(GetAgentDisplayName(left.agentTypeID), GetAgentDisplayName(right.agentTypeID), StringComparison.Ordinal);
        if (agentCompare != 0)
            return agentCompare;

        return string.Compare(GetHierarchyPath(left.transform), GetHierarchyPath(right.transform), StringComparison.Ordinal);
    }

    private static string GetAgentDisplayName(int agentTypeId)
    {
        string name = NavMesh.GetSettingsNameFromID(agentTypeId);
        if (string.IsNullOrEmpty(name))
            name = "Unknown Agent";

        return $"{name} ({agentTypeId})";
    }

    private void RefreshItemAgentTypeId()
    {
        ItemSpawner[] itemSpawners = Resources.FindObjectsOfTypeAll<ItemSpawner>();
        for (int i = 0; i < itemSpawners.Length; i++)
        {
            ItemSpawner spawner = itemSpawners[i];
            if (spawner == null || EditorUtility.IsPersistent(spawner))
                continue;

            SerializedObject serialized = new SerializedObject(spawner);
            SerializedProperty agentType = serialized.FindProperty("navMeshAgentTypeId");
            if (agentType == null)
                continue;

            itemAgentTypeId = agentType.intValue;
            return;
        }

        itemAgentTypeId = DefaultItemAgentTypeId;
    }

    private string GetGroupDisplayName(int agentTypeId)
    {
        string displayName = GetAgentDisplayName(agentTypeId);
        if (agentTypeId == itemAgentTypeId)
            return "Item Spawn / " + displayName;

        return displayName;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
            return string.Empty;

        Stack<string> names = new Stack<string>();
        Transform current = transform;
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names.ToArray());
    }

    private sealed class AgentSurfaceGroup
    {
        public readonly int AgentTypeId;
        public readonly List<NavMeshSurface> Surfaces = new List<NavMeshSurface>();
        public int EnabledCount;

        public AgentSurfaceGroup(int agentTypeId)
        {
            AgentTypeId = agentTypeId;
        }

        public string DisplayName => GetAgentDisplayName(AgentTypeId);
    }
}
