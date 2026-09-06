using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

[CustomEditor(typeof(DungeonNavMeshArea))]
[CanEditMultipleObjects]
public sealed class DungeonNavMeshAreaEditor : Editor
{
    private readonly BoxBoundsHandle _boxHandle = new BoxBoundsHandle();
    private string _snapMessage;
    private MessageType _snapMessageType = MessageType.None;
    private SerializedProperty _areaType;
    private SerializedProperty _shape;
    private SerializedProperty _includeInRuntimeBake;
    private SerializedProperty _center;
    private SerializedProperty _size;

    private void OnEnable()
    {
        _areaType = serializedObject.FindProperty("areaType");
        _shape = serializedObject.FindProperty("shape");
        _includeInRuntimeBake = serializedObject.FindProperty("includeInRuntimeBake");
        _center = serializedObject.FindProperty("center");
        _size = serializedObject.FindProperty("size");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            _areaType.enumValueIndex == (int)DungeonNavMeshArea.AreaType.Walkable
                ? "WALKABLE (green): monsters may walk only on authored green areas when the room has at least one."
                : "NOT WALKABLE (red): this volume is always removed from the runtime NavMesh.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            Color oldBackground = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.35f, 1f, 0.45f);
            if (GUILayout.Button("WALKABLE", GUILayout.Height(30f)))
                _areaType.enumValueIndex = (int)DungeonNavMeshArea.AreaType.Walkable;

            GUI.backgroundColor = new Color(1f, 0.35f, 0.3f);
            if (GUILayout.Button("NOT WALKABLE", GUILayout.Height(30f)))
                _areaType.enumValueIndex = (int)DungeonNavMeshArea.AreaType.NotWalkable;
            GUI.backgroundColor = oldBackground;
        }

        EditorGUILayout.PropertyField(_includeInRuntimeBake, new GUIContent("Use In Runtime Bake"));
        EditorGUILayout.PropertyField(_shape, new GUIContent("Shape"));

        if (_shape.enumValueIndex == (int)DungeonNavMeshArea.ShapeType.Box)
        {
            EditorGUILayout.PropertyField(_center);
            EditorGUILayout.PropertyField(_size);
            EditorGUILayout.HelpBox("Resize this colored box directly in the Scene view.", MessageType.None);
            DrawSnapControls();
        }
        else
        {
            DrawAttachedColliderStatus();
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSnapControls()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Snap Box Face", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Only the chosen face moves. The opposite face stays fixed. The selected face snaps to the nearest enabled, non-trigger Collider surface in the room. Red Not Walkable boxes overlap that surface by 0.02 m so the runtime bake cannot miss a touching boundary.",
            MessageType.None);

        if (targets.Length != 1)
        {
            EditorGUILayout.HelpBox("Select one Dungeon NavMesh Area to use face snapping.", MessageType.Info);
            return;
        }

        DungeonNavMeshArea area = (DungeonNavMeshArea)target;
        if (EditorUtility.IsPersistent(area))
        {
            EditorGUILayout.HelpBox("Open the prefab in Prefab Mode before using face snapping.", MessageType.Info);
            return;
        }

        DrawSnapRow("X", DungeonNavMeshAreaSnapFace.NegativeX, DungeonNavMeshAreaSnapFace.PositiveX);
        DrawSnapRow("Y", DungeonNavMeshAreaSnapFace.NegativeY, DungeonNavMeshAreaSnapFace.PositiveY);
        DrawSnapRow("Z", DungeonNavMeshAreaSnapFace.NegativeZ, DungeonNavMeshAreaSnapFace.PositiveZ);

        if (!string.IsNullOrEmpty(_snapMessage))
            EditorGUILayout.HelpBox(_snapMessage, _snapMessageType);
    }

    private void DrawSnapRow(
        string axis,
        DungeonNavMeshAreaSnapFace negativeFace,
        DungeonNavMeshAreaSnapFace positiveFace)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("-" + axis))
                SnapSelectedFace(negativeFace);
            if (GUILayout.Button("+" + axis))
                SnapSelectedFace(positiveFace);
        }
    }

    private void SnapSelectedFace(DungeonNavMeshAreaSnapFace face)
    {
        serializedObject.ApplyModifiedProperties();
        DungeonNavMeshArea area = (DungeonNavMeshArea)target;
        if (!DungeonNavMeshAreaSnapUtility.TryCalculateSnap(area, face, out DungeonNavMeshAreaSnapResult result, out string failure))
        {
            _snapMessage = failure;
            _snapMessageType = MessageType.Warning;
            return;
        }

        Undo.RecordObject(area, "Snap Dungeon NavMesh Area " + face);
        area.SetBoxBounds(result.Center, result.Size);
        PrefabUtility.RecordPrefabInstancePropertyModifications(area);
        EditorUtility.SetDirty(area);
        serializedObject.Update();
        SceneView.RepaintAll();

        _snapMessage = $"{face} snapped to {result.ColliderPath} (moved {result.FaceMovement:F3} m).";
        _snapMessageType = MessageType.Info;
    }

    private void DrawAttachedColliderStatus()
    {
        if (targets.Length != 1)
            return;

        DungeonNavMeshArea area = (DungeonNavMeshArea)target;
        Collider[] colliders = area.GetComponents<Collider>();
        int valid = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && colliders[i].enabled && !colliders[i].isTrigger)
                valid++;
        }

        EditorGUILayout.HelpBox(
            valid > 0
                ? $"Using {valid} enabled Collider component(s) on this object."
                : "No enabled non-trigger Collider is attached. Choose Box or add a Collider.",
            valid > 0 ? MessageType.None : MessageType.Warning);
    }

    private void OnSceneGUI()
    {
        DungeonNavMeshArea area = target as DungeonNavMeshArea;
        if (area == null)
            return;

        if (area.Shape != DungeonNavMeshArea.ShapeType.Box)
            return;

        Color color = area.IsWalkable
            ? new Color(0.15f, 0.95f, 0.3f, 1f)
            : new Color(1f, 0.2f, 0.15f, 1f);

        Handles.color = color;
        using (new Handles.DrawingScope(area.transform.localToWorldMatrix))
        {
            _boxHandle.center = area.Center;
            _boxHandle.size = area.Size;
            EditorGUI.BeginChangeCheck();
            _boxHandle.DrawHandle();
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(area, "Resize Dungeon NavMesh Area");
                area.SetBoxBounds(_boxHandle.center, _boxHandle.size);
                EditorUtility.SetDirty(area);
            }
        }
    }

    [MenuItem("GameObject/Dungeon NavMesh/Create Walkable Area", false, 20)]
    private static void CreateWalkable(MenuCommand command)
    {
        CreateArea("NavMesh_Walkable", DungeonNavMeshArea.AreaType.Walkable, command);
    }

    [MenuItem("GameObject/Dungeon NavMesh/Create Not Walkable Area", false, 21)]
    private static void CreateNotWalkable(MenuCommand command)
    {
        CreateArea("NavMesh_NotWalkable", DungeonNavMeshArea.AreaType.NotWalkable, command);
    }

    private static void CreateArea(string objectName, DungeonNavMeshArea.AreaType type, MenuCommand command)
    {
        GameObject areaObject = new GameObject(objectName);
        GameObjectUtility.SetParentAndAlign(areaObject, command.context as GameObject);
        Undo.RegisterCreatedObjectUndo(areaObject, "Create Dungeon NavMesh Area");

        DungeonNavMeshArea area = Undo.AddComponent<DungeonNavMeshArea>(areaObject);
        Vector3 defaultSize = type == DungeonNavMeshArea.AreaType.Walkable
            ? new Vector3(4f, 0.2f, 4f)
            : new Vector3(2f, 2.5f, 0.3f);
        area.ConfigureBox(type, Vector3.zero, defaultSize);
        Selection.activeGameObject = areaObject;
    }
}
