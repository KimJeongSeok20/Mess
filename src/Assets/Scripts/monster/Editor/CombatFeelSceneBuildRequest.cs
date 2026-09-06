using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CombatFeelSceneBuildRequest
{
    private static bool _queued;
    private static string RequestPath => Path.Combine(
        Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty,
        "Temp",
        "CombatFeelSceneBuild.request");

    static CombatFeelSceneBuildRequest()
    {
        QueueIfRequested();
    }

    private static void QueueIfRequested()
    {
        if (_queued || !File.Exists(RequestPath))
            return;

        _queued = true;
        EditorApplication.delayCall += TryBuild;
    }

    private static void TryBuild()
    {
        if (!File.Exists(RequestPath))
        {
            _queued = false;
            return;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += TryBuild;
            return;
        }

        try
        {
            string rebuild = MonsterTestSceneSetup.RebuildScene();
            string validation = MonsterTestSceneSetup.ValidateCombatFeelScene();
            File.Delete(RequestPath);
            Debug.Log($"[CombatFeelSceneBuildRequest] {rebuild}\n{validation}");
        }
        catch (Exception exception)
        {
            _queued = false;
            Debug.LogException(exception);
        }
    }
}
