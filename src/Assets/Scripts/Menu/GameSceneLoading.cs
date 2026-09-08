using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Foreground scene transitions and build startup timings, shared by solo and Steam.</summary>
public static class GameSceneLoading
{
    private static string _scenePath;
    private static float _startedAt;
    private static UnityEngine.ThreadPriority _previousPriority;
    private static bool _loading;
    private static bool _waitingForPlayer;

    public static float ElapsedSeconds => Time.realtimeSinceStartup - _startedAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        RestorePriority();
        _waitingForPlayer = false;
    }

    public static AsyncOperation Begin(string scenePath)
    {
        if (_loading) throw new InvalidOperationException("A scene transition is already running.");
        _scenePath = scenePath;
        _startedAt = Time.realtimeSinceStartup;
        _previousPriority = Application.backgroundLoadingPriority;
        _loading = true;
        _waitingForPlayer = scenePath.EndsWith("/StartMap.unity", StringComparison.Ordinal);
        SceneManager.sceneLoaded += OnSceneLoaded;
        // The baseline flag is for reproducible standalone measurements of the previous budget.
        if (!Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-loading-baseline"))
            Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.High;
        Debug.Log($"[StartupLoading] begin scene={scenePath} priority={Application.backgroundLoadingPriority}");
        try
        {
            var operation = SceneManager.LoadSceneAsync(scenePath);
            if (operation == null) throw new InvalidOperationException("Unity did not start the scene load.");
            return operation;
        }
        catch
        {
            RestorePriority();
            _waitingForPlayer = false;
            throw;
        }
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.path != _scenePath) return;
        Debug.Log($"[StartupLoading] scene-ready seconds={ElapsedSeconds:F3} scene={scene.name}");
        RestorePriority();
    }

    private static void RestorePriority()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (!_loading) return;
        Application.backgroundLoadingPriority = _previousPriority;
        _loading = false;
    }

    public static void PlayerReady()
    {
        if (!_waitingForPlayer) return;
        _waitingForPlayer = false;
        Debug.Log($"[StartupLoading] player-ready seconds={ElapsedSeconds:F3} priorityRestored={Application.backgroundLoadingPriority}");
    }
}
