using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Behavior;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ClownBehaviorGraphBuilder
{
    private const string SourceGraphPath = "Assets/Monster/smily/smily-horror-monster_cc_attribution/Behavior Graph.asset";
    private const string TargetGraphPath = "Assets/Monster/Clown/ClownBehaviorGraph.asset";
    private const string ClownPrefabPath = "Assets/Clown/Prefab/Clown skin2 combined.prefab";
    private const string ClownTestScenePath = "Assets/Clown/Scenes/ClownTest.unity";
    private const string ClownSceneInstanceName = "ClownSkin2_Test";

    public static string BuildAndAssign()
    {
        EnsureGraphAssetExists();

        UnityEngine.Object authoringGraph = AssetDatabase.LoadMainAssetAtPath(TargetGraphPath);
        if (authoringGraph == null)
            return "Failed to load clown behavior authoring graph";

        string rebuildResult = RebuildGraph(authoringGraph);
        UnityEngine.Object runtimeGraph = LoadRuntimeGraph();
        if (runtimeGraph == null)
            return rebuildResult + " | Failed to load clown runtime graph";

        string prefabResult = AssignGraphToPrefab(runtimeGraph);
        string sceneResult = AssignGraphToScene(runtimeGraph);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return rebuildResult + " | " + prefabResult + " | " + sceneResult;
    }

    private static void EnsureGraphAssetExists()
    {
        if (AssetDatabase.LoadMainAssetAtPath(TargetGraphPath) != null)
            return;

        AssetDatabase.CopyAsset(SourceGraphPath, TargetGraphPath);
        AssetDatabase.ImportAsset(TargetGraphPath, ImportAssetOptions.ForceUpdate);
    }

    private static string RebuildGraph(UnityEngine.Object authoringGraph)
    {
        Type graphType = authoringGraph.GetType();
        PropertyInfo nodesProperty = graphType.GetProperty("Nodes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo deleteNodeMethod = graphType.GetMethod("DeleteNode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo createNodeMethod = graphType.GetMethod("CreateNode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo connectEdgeMethod = graphType.GetMethod("ConnectEdge", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo addNodeToSequenceMethod = graphType.GetMethod("AddNodeToSequence", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo createNodeModelsInfoCacheMethod = graphType.GetMethod("CreateNodeModelsInfoCache", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo rebuildAndSaveMethod = graphType.GetMethod("RebuildAndSave", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo saveAssetMethod = graphType.GetMethod("SaveAsset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (nodesProperty == null || deleteNodeMethod == null || createNodeMethod == null || connectEdgeMethod == null || addNodeToSequenceMethod == null)
            return "Missing behavior graph reflection hooks";

        IList nodes = ((IEnumerable)nodesProperty.GetValue(authoringGraph)).Cast<object>().ToList();
        object existingStartNode = null;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (existingStartNode == null)
            {
                string typeName = nodes[i]?.GetType().Name ?? string.Empty;
                if (typeName.Contains("Start"))
                {
                    existingStartNode = nodes[i];
                    continue;
                }
            }

            deleteNodeMethod.Invoke(authoringGraph, new[] { nodes[i] });
        }

        Type startType = FindType("Unity.Behavior.Start", "Start");
        Type driverType = FindType("ClownGraphDriverAction");

        if (startType == null || driverType == null)
            return $"Missing node types start={startType != null} driver={driverType != null}";

        createNodeModelsInfoCacheMethod?.Invoke(authoringGraph, null);

        object startNode = existingStartNode ?? CreateNodeSafe(createNodeMethod, authoringGraph, startType, new Vector2(100f, 150f), "Start");
        object driverNode = CreateNodeSafe(createNodeMethod, authoringGraph, driverType, new Vector2(420f, 150f), "Driver");

        if (startNode is string startError) return startError;
        if (driverNode is string driverError) return driverError;

        SetStartRepeat(startNode, true);

        if (!ConnectNodes(authoringGraph, connectEdgeMethod, startNode, driverNode))
            return "Failed to connect root graph ports";

        rebuildAndSaveMethod?.Invoke(authoringGraph, null);
        saveAssetMethod?.Invoke(authoringGraph, null);
        EditorUtility.SetDirty(authoringGraph);

        int finalCount = ((IEnumerable)nodesProperty.GetValue(authoringGraph)).Cast<object>().Count();
        return $"Rebuilt clown graph nodes={finalCount}";
    }

    private static UnityEngine.Object LoadRuntimeGraph()
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(TargetGraphPath);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is BehaviorGraph)
                return assets[i];
        }

        return null;
    }

    private static string AssignGraphToPrefab(UnityEngine.Object runtimeGraph)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ClownPrefabPath);
        try
        {
            BehaviorGraphAgent agent = root.GetComponent<BehaviorGraphAgent>();
            if (agent == null)
                return "Clown prefab missing BehaviorGraphAgent";

            AssignGraphToAgent(agent, runtimeGraph);
            PrefabUtility.SaveAsPrefabAsset(root, ClownPrefabPath);
            return "Assigned graph to clown prefab";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static string AssignGraphToScene(UnityEngine.Object runtimeGraph)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ClownTestScenePath) == null)
            return "Clown test scene not found";

        var scene = EditorSceneManager.OpenScene(ClownTestScenePath, OpenSceneMode.Single);
        GameObject clown = GameObject.Find(ClownSceneInstanceName);
        if (clown == null)
            return "Clown test scene instance missing";

        BehaviorGraphAgent agent = clown.GetComponent<BehaviorGraphAgent>();
        if (agent == null)
            return "Clown scene instance missing BehaviorGraphAgent";

        AssignGraphToAgent(agent, runtimeGraph);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Assigned graph to clown test scene";
    }

    private static void AssignGraphToAgent(BehaviorGraphAgent agent, UnityEngine.Object runtimeGraph)
    {
        SerializedObject serializedObject = new SerializedObject(agent);
        SerializedProperty graphProperty = serializedObject.FindProperty("m_Graph");
        if (graphProperty != null)
        {
            graphProperty.objectReferenceValue = runtimeGraph;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        agent.enabled = false;
        EditorUtility.SetDirty(agent);
    }

    private static object GetDefaultPort(object node, string methodName)
    {
        MethodInfo method = node.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
            return null;

        object[] args = { null };
        bool ok = (bool)method.Invoke(node, args);
        return ok ? args[0] : null;
    }

    private static bool ConnectNodes(object graph, MethodInfo connectEdgeMethod, object fromNode, object toNode)
    {
        object outPort = GetDefaultPort(fromNode, "TryDefaultOutputPortModel");
        object inPort = GetDefaultPort(toNode, "TryDefaultInputPortModel");
        if (outPort == null || inPort == null)
            return false;

        connectEdgeMethod.Invoke(graph, new object[] { outPort, inPort });
        return true;
    }

    private static void SetStartRepeat(object startNode, bool repeat)
    {
        if (startNode == null)
            return;

        FieldInfo repeatField = startNode.GetType().GetField("Repeat", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (repeatField != null)
            repeatField.SetValue(startNode, repeat);
    }

    private static object CreateNodeSafe(MethodInfo createNodeMethod, object graph, Type type, Vector2 position, string label)
    {
        try
        {
            return createNodeMethod.Invoke(graph, new object[] { type, position, null, Array.Empty<object>() });
        }
        catch (Exception ex)
        {
            string message = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return $"Failed to create {label} node ({type?.FullName ?? "null"}): {message}";
        }
    }

    private static Type FindType(params string[] names)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int a = 0; a < assemblies.Length; a++)
        {
            Type[] types;
            try
            {
                types = assemblies[a].GetTypes();
            }
            catch
            {
                continue;
            }

            for (int t = 0; t < types.Length; t++)
            {
                for (int n = 0; n < names.Length; n++)
                {
                    if (types[t].FullName == names[n] || types[t].Name == names[n])
                        return types[t];
                }
            }
        }

        return null;
    }
}
