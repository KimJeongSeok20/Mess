using System;
using System.Collections;
using System.IO;
using System.Reflection;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>Opt-in standalone smoke test using an isolated copy of the user's checkpoint.</summary>
public sealed class StartupLoadingValidationRunner : MonoBehaviour
{
    private string _output;
    private string _userSavePath;
    private string _userSaveContents;
    private string _dungeonFailure;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-startup-validation");
        if (index < 0 || index + 1 >= args.Length) return;
        var runner = new GameObject(nameof(StartupLoadingValidationRunner)).AddComponent<StartupLoadingValidationRunner>();
        DontDestroyOnLoad(runner.gameObject);
        runner._output = args[index + 1];
        Directory.CreateDirectory(runner._output);
        runner._userSavePath = RunSaveService.SavePath;
        runner._userSaveContents = File.Exists(runner._userSavePath) ? File.ReadAllText(runner._userSavePath) : null;
        RunSaveService.ValidationSaveDirectory = runner._output;
        if (runner._userSaveContents != null) File.WriteAllText(RunSaveService.SavePath, runner._userSaveContents);
        runner.StartCoroutine(runner.Validate());
    }

    private IEnumerator Validate()
    {
        Directory.CreateDirectory(_output);
        yield return new WaitForSecondsRealtime(3f);
        if (SceneManager.GetActiveScene().name != "MainMenu" || !RunSaveService.HasSave)
        { Finish("FAIL: validation requires MainMenu and an existing solo checkpoint.", 1); yield break; }
        string checkpointBefore = File.ReadAllText(RunSaveService.SavePath);
        var menu = FindFirstObjectByType<GameMenuController>();
        var button = (UnityEngine.UI.Button)typeof(GameMenuController)
            .GetField("continueButton", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(menu);
        ScreenCapture.CaptureScreenshot(Path.Combine(_output, "title.png"));
        yield return null;
        var prior = Application.backgroundLoadingPriority;
        float started = Time.realtimeSinceStartup;
        button.onClick.Invoke();
        float deadline = started + 180f;
        while (Time.realtimeSinceStartup < deadline)
        {
            var save = RunSaveService.Instance;
            var player = NetworkPlayer.Local;
            if (SceneManager.GetActiveScene().name == "StartMap" && save != null && save.IsReady
                && player != null && player.GetComponent<PlayerInput>().inputIsActive && !GameMenuController.IsOpen)
            {
                var manager = NetworkManager.main;
                int dungeonLightingSetsAtCamp = Resources.FindObjectsOfTypeAll<DungeonTileRotationLightingSetV2>().Length;
                bool requireDeferred = Array.IndexOf(Environment.GetCommandLineArgs(), "-validate-dungeon") >= 0;
                bool passed = manager != null && manager.transport is LocalTransport
                    && manager.isServer && manager.isClient && manager.playerCount == 1
                    && Application.backgroundLoadingPriority == prior
                    && (SteamManager.Instance == null || !SteamManager.Instance.Initialized)
                    && (!requireDeferred || dungeonLightingSetsAtCamp == 0)
                    && File.ReadAllText(RunSaveService.SavePath) == checkpointBefore;
                string result = $"{(passed ? "PASS" : "FAIL")} seconds={Time.realtimeSinceStartup - started:F3} "
                    + $"transport={manager?.transport?.GetType().Name} players={manager?.playerCount} "
                    + $"input={player.GetComponent<PlayerInput>().inputIsActive} priority={Application.backgroundLoadingPriority} "
                    + $"checkpointUnchanged={File.ReadAllText(RunSaveService.SavePath) == checkpointBefore} "
                    + $"textureMB={Texture.currentTextureMemory / 1048576} streamingTextures={Texture.streamingTextureCount} "
                    + $"dungeonLightingSetsAtCamp={dungeonLightingSetsAtCamp}";
                ScreenCapture.CaptureScreenshot(Path.Combine(_output, "game.png"));
                yield return new WaitForSecondsRealtime(5f);
                ScreenCapture.CaptureScreenshot(Path.Combine(_output, "game-settled.png"));
                if (!passed) { Finish(result, 1); yield break; }
                File.WriteAllText(Path.Combine(_output, "continue-result.txt"), result);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-validate-dungeon") >= 0)
                {
                    yield return ValidateDungeon();
                    if (_dungeonFailure != null) { Finish("FAIL: " + _dungeonFailure, 1); yield break; }
                }
                // Exercise normal leave/re-entry and a fresh solo checkpoint without touching the user's file.
                menu = FindFirstObjectByType<GameMenuController>();
                Click(menu, "returnButton");
                Click(menu, "confirmButton");
                float returnDeadline = Time.realtimeSinceStartup + 30f;
                while (SceneManager.GetActiveScene().name != "MainMenu" && Time.realtimeSinceStartup < returnDeadline)
                    yield return null;
                if (SceneManager.GetActiveScene().name != "MainMenu")
                { Finish("FAIL: return to title timed out. " + result, 1); yield break; }
                yield return null;
                menu = FindFirstObjectByType<GameMenuController>();
                Click(menu, "newGameButton");
                Click(menu, "confirmButton");
                float newDeadline = Time.realtimeSinceStartup + 180f;
                while (Time.realtimeSinceStartup < newDeadline)
                {
                    var newSave = RunSaveService.Instance;
                    var newPlayer = NetworkPlayer.Local;
                    if (SceneManager.GetActiveScene().name == "StartMap" && newSave != null && newSave.IsReady
                        && newPlayer != null && newPlayer.GetComponent<PlayerInput>().inputIsActive && !GameMenuController.IsOpen)
                    {
                        bool newPassed = NetworkManager.main.transport is LocalTransport && NetworkManager.main.playerCount == 1
                            && File.Exists(RunSaveService.SavePath) && TimeManager.CurrentDay == 1
                            && File.ReadAllText(_userSavePath) == _userSaveContents;
                        Finish((newPassed ? result : "FAIL: return/new-game check failed. " + result)
                            + $" returnAndNewGame={newPassed} userSaveUnchanged={File.ReadAllText(_userSavePath) == _userSaveContents}", newPassed ? 0 : 1);
                        yield break;
                    }
                    if (newSave != null && !string.IsNullOrEmpty(newSave.LastError))
                    { Finish("FAIL: new game: " + newSave.LastError, 1); yield break; }
                    yield return null;
                }
                Finish("FAIL: new game timed out. " + result, 1);
                yield break;
            }
            if (save != null && !string.IsNullOrEmpty(save.LastError))
            { Finish("FAIL: " + save.LastError, 1); yield break; }
            yield return null;
        }
        Finish("FAIL: startup exceeded 180 seconds.", 1);
    }

    private static void Click(GameMenuController menu, string field)
    {
        ((UnityEngine.UI.Button)typeof(GameMenuController).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(menu)).onClick.Invoke();
    }

    private IEnumerator ValidateDungeon(bool verifyRegeneration = true)
    {
        var controller = FindFirstObjectByType<NetworkDungeonController>();
        if (controller == null || !controller.MapList.UsesDeferredLoading)
        { _dungeonFailure = "Deferred dungeon catalog is missing."; yield break; }
        var campLightmaps = LightmapSettings.lightmaps;
        var campLightmapsMode = LightmapSettings.lightmapsMode;
        string resultFile = verifyRegeneration ? "dungeon-result.txt" : "dungeon-regeneration-result.txt";
        UnityEngine.Random.InitState(verifyRegeneration ? 82416 : 82417);
        float started = Time.realtimeSinceStartup;
        var clock = TimeManager.Active;
        float initialHour = clock.GetCurrentTime();
        clock.StartTimeClockRpc();
        float deadline = started + 240f;
        while (!controller.IsLocalDungeonReady && Time.realtimeSinceStartup < deadline)
        {
            if (!string.IsNullOrEmpty(controller.MapLoadError))
            { _dungeonFailure = controller.MapLoadError; yield break; }
            if (clock.IsClockRunning() || Mathf.Abs(clock.GetCurrentTime() - initialHour) > 0.01f)
            { _dungeonFailure = "Day time advanced before the dungeon was ready."; yield break; }
            yield return null;
        }
        if (!controller.IsLocalDungeonReady)
        { _dungeonFailure = "Dungeon generation/readiness timed out."; yield break; }
        float clockDeadline = Time.realtimeSinceStartup + 10f;
        while (!clock.IsClockRunning() && Time.realtimeSinceStartup < clockDeadline) yield return null;
        if (!clock.IsClockRunning() || clock.IsPreparingDungeon)
        { _dungeonFailure = "Ready acknowledgement did not release the day clock."; yield break; }
        var tiles = FindObjectsByType<DungeonTileRotationSelectorV2>(FindObjectsSortMode.None);
        if (tiles.Length == 0) { _dungeonFailure = "No generated V2 tiles."; yield break; }
        foreach (var tile in tiles)
        {
            var expected = tile.LightingSet.Resolve(tile.transform.eulerAngles.y);
            var switcher = tile.GetComponent<DungeonTileLightmapSwitcher>();
            var data = tile.GetComponent<DungeonTilePowerBakeSet>();
            if (expected == null || data.Power100Bake != expected.power100 ||
                data.Power00Bake != (switcher.SupportsPowerToggle ? expected.power0 : expected.power100))
            { _dungeonFailure = "Incorrect rotation/power bake on " + tile.name; yield break; }
            var original = switcher.CurrentPowerLevel;
            switcher.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P0);
            if (switcher.CurrentBakeData != data.Power00Bake)
            { _dungeonFailure = "P0 bake mismatch on " + tile.name; yield break; }
            switcher.SetPowerLevel(DungeonTileLightmapSwitcher.PowerLevel.P100);
            if (switcher.CurrentBakeData != data.Power100Bake)
            { _dungeonFailure = "P100 bake mismatch on " + tile.name; yield break; }
            switcher.SetPowerLevel(original);
        }
        string result = $"PASS index={controller.CurrentFlowIndex} seed={controller.CurrentSeed} tiles={tiles.Length} "
            + $"seconds={Time.realtimeSinceStartup - started:F3} rotationsAndPower=True clockWaitedForReadiness=True catalogMaps={controller.MapList.Count}";
        File.WriteAllText(Path.Combine(_output, resultFile), result);
        Debug.Log("[DungeonValidation] " + result);
        var player = NetworkPlayer.Local;
        var entrance = FindFirstObjectByType<DungeonEntrance>();
        var interaction = FindFirstObjectByType<InteractionManager>(); // Scene-owned GameManager component.
        var zone = FindFirstObjectByType<DungeonZoneManager>();
        if (DungeonStartPoint.Instance == null || entrance == null || interaction == null || interaction.CurrentCamera == null)
        { _dungeonFailure = "The real dungeon entrance interaction is unavailable."; yield break; }
        entrance.Interact(interaction);
        if (Vector3.Distance(player.transform.position, DungeonStartPoint.Instance.position) > 0.1f || zone == null || !zone.IsInDungeon)
        { _dungeonFailure = "Entrance did not teleport the player and apply the dungeon zone."; yield break; }
        yield return new WaitForSecondsRealtime(2f);
        ScreenCapture.CaptureScreenshot(Path.Combine(_output, verifyRegeneration ? "dungeon.png" : "dungeon-regenerated.png"));
        File.AppendAllText(Path.Combine(_output, resultFile), " entranceAndZone=True");
        yield return null;
        int day = TimeManager.CurrentDay;
        clock.ServerForceEndDay("Deferred loading lifecycle validation");
        float clearDeadline = Time.realtimeSinceStartup + 30f;
        while ((!clock.IsSafeMorningCheckpoint || TimeManager.CurrentDay <= day) && Time.realtimeSinceStartup < clearDeadline)
            yield return null;
        yield return null; // Let deferred destruction release the old room components.
        if (!clock.IsSafeMorningCheckpoint || TimeManager.CurrentDay <= day)
        { _dungeonFailure = "Day reset did not reach a safe camp checkpoint."; yield break; }
        var clearedMaps = LightmapSettings.lightmaps;
        bool campPreserved = clearedMaps.Length == campLightmaps.Length && LightmapSettings.lightmapsMode == campLightmapsMode;
        for (int i = 0; campPreserved && i < campLightmaps.Length; i++)
            campPreserved = clearedMaps[i].lightmapColor == campLightmaps[i].lightmapColor
                && clearedMaps[i].lightmapDir == campLightmaps[i].lightmapDir
                && clearedMaps[i].shadowMask == campLightmaps[i].shadowMask;
        var registry = FindFirstObjectByType<DungeonTileProbeRegistry>();
        var installer = FindFirstObjectByType<DungeonRoomLocalLightShare.RoomLocalStartMapInstaller>();
        int outgoingCount = installer == null ? 0 : ((System.Collections.IDictionary)installer.GetType()
            .GetField("liveOutgoing", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(installer)).Count;
        if (!campPreserved || (registry != null && registry.TileSetCount != 0) || outgoingCount != 0)
        { _dungeonFailure = "Old dungeon lightmaps or probe/portal caches survived clearing."; yield break; }
        File.AppendAllText(Path.Combine(_output, resultFile), $" clearRestoredCamp=True campLightmaps={campLightmaps.Length} probeAndPortalCachesCleared=True");
        if (verifyRegeneration) yield return ValidateDungeon(false);
    }

    private void Finish(string result, int exitCode)
    {
        Debug.Log("[StartupValidation] " + result);
        File.WriteAllText(Path.Combine(_output, "result.txt"), result);
        Application.Quit(exitCode);
    }
}
