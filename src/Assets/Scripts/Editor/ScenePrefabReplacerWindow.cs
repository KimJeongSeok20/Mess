using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class ScenePrefabReplacerWindow : EditorWindow
{
    private GameObject replacementPrefab;
    private bool keepName = true;
    private bool keepSiblingIndex = true;
    private bool keepActiveState = true;

    [MenuItem("Tools/StillWorking/Scene Prefab Replacer")]
    private static void OpenWindow()
    {
        GetWindow<ScenePrefabReplacerWindow>("Prefab Replacer");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Replace selected scene objects with one prefab asset.", EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space();

        replacementPrefab = (GameObject)EditorGUILayout.ObjectField("Replacement Prefab", replacementPrefab, typeof(GameObject), false);

        keepName = EditorGUILayout.ToggleLeft("Keep original object name", keepName);
        keepSiblingIndex = EditorGUILayout.ToggleLeft("Keep sibling index", keepSiblingIndex);
        keepActiveState = EditorGUILayout.ToggleLeft("Keep active state", keepActiveState);

        EditorGUILayout.Space();

        if (GUILayout.Button("Use selected project prefab"))
        {
            TryAssignSelectedPrefab();
        }

        using (new EditorGUI.DisabledScope(!CanReplace()))
        {
            if (GUILayout.Button("Replace selected scene objects"))
            {
                ReplaceSelectedSceneObjects();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Select scene objects in Hierarchy, set a prefab from Project, then run replace.", MessageType.Info);
    }

    private bool CanReplace()
    {
        return replacementPrefab != null && IsPrefabAsset(replacementPrefab) && Selection.gameObjects.Length > 0;
    }

    private static bool IsPrefabAsset(GameObject gameObject)
    {
        return gameObject != null && EditorUtility.IsPersistent(gameObject) && PrefabUtility.IsPartOfPrefabAsset(gameObject);
    }

    private void TryAssignSelectedPrefab()
    {
        if (Selection.activeObject is GameObject selected && IsPrefabAsset(selected))
        {
            replacementPrefab = selected;
            return;
        }

        EditorUtility.DisplayDialog("Prefab Replacer", "Select a prefab asset in Project first.", "OK");
    }

    private void ReplaceSelectedSceneObjects()
    {
        if (!IsPrefabAsset(replacementPrefab))
        {
            EditorUtility.DisplayDialog("Prefab Replacer", "Replacement Prefab must be a prefab asset from Project.", "OK");
            return;
        }

        List<GameObject> selectedSceneObjects = Selection.gameObjects
            .Where(obj => obj != null && obj.scene.IsValid() && !EditorUtility.IsPersistent(obj))
            .ToList();

        if (selectedSceneObjects.Count == 0)
        {
            EditorUtility.DisplayDialog("Prefab Replacer", "Select at least one scene object in Hierarchy.", "OK");
            return;
        }

        List<GameObject> replaceTargets = GetTopLevelSelection(selectedSceneObjects);
        List<Object> createdObjects = new List<Object>(replaceTargets.Count);

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Replace Scene Objects With Prefab");

        foreach (GameObject target in replaceTargets)
        {
            Transform targetTransform = target.transform;
            Transform parent = targetTransform.parent;
            int siblingIndex = targetTransform.GetSiblingIndex();
            Vector3 localPosition = targetTransform.localPosition;
            Quaternion localRotation = targetTransform.localRotation;
            Vector3 localScale = targetTransform.localScale;
            bool activeState = target.activeSelf;
            string objectName = target.name;

            GameObject instance = parent != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(replacementPrefab, parent)
                : (GameObject)PrefabUtility.InstantiatePrefab(replacementPrefab, target.scene);

            Undo.RegisterCreatedObjectUndo(instance, "Create replacement prefab instance");

            Transform instanceTransform = instance.transform;
            instanceTransform.localPosition = localPosition;
            instanceTransform.localRotation = localRotation;
            instanceTransform.localScale = localScale;

            if (keepSiblingIndex && parent != null)
            {
                instanceTransform.SetSiblingIndex(siblingIndex);
            }

            if (keepName)
            {
                instance.name = objectName;
            }

            if (keepActiveState)
            {
                instance.SetActive(activeState);
            }

            createdObjects.Add(instance);
            Undo.DestroyObjectImmediate(target);
        }

        Undo.CollapseUndoOperations(undoGroup);
        Selection.objects = createdObjects.ToArray();
        Debug.Log($"Prefab Replacer: replaced {replaceTargets.Count} object(s) with '{replacementPrefab.name}'.");
    }

    private static List<GameObject> GetTopLevelSelection(List<GameObject> selectedObjects)
    {
        HashSet<Transform> selectedTransforms = new HashSet<Transform>(selectedObjects.Select(obj => obj.transform));
        List<GameObject> result = new List<GameObject>();

        foreach (GameObject gameObject in selectedObjects)
        {
            Transform parent = gameObject.transform.parent;
            if (parent == null || !selectedTransforms.Contains(parent))
            {
                result.Add(gameObject);
            }
        }

        return result;
    }
}
