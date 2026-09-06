using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class FixDoorPrefabs : EditorWindow
{
    [MenuItem("Tools/Door Utilities/Fix Door Prefabs (disabled)")]
    static void RunFix()
    {
        EditorUtility.DisplayDialog("Fix Door Prefabs", "Tool disabled: NavMesh-related prefab fixes are turned off.", "OK");
    }
}
