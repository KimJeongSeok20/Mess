using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Opt-in observer for an existing Play Mode session. No inputs, rewards, RNG, or network state writes.
/// Editor-only: no scene components, build changes, or automatic recording on startup.
/// </summary>
public static class GameplaySessionRecorder
{
    private const string OutputRoot = "D:/what/deps/temp/StillWorkingPlaytests";
    private static StreamWriter _writer;
    private static double _started, _nextSample, _nextLookup;
    private static string _session, _source, _revision;
    private static NetworkDungeonController _dungeon;
    private static CurrencyManager _currency;
    private static DungeonMonsterSpawner _spawner;
    private static MonsterHealth[] _monsters = Array.Empty<MonsterHealth>();
    public static bool IsRecording => _writer != null;
    public static string LastPath { get; private set; }

    [Serializable] private sealed class PlayerSample
    {
        public string id;
        public bool owner, hasVitals, dead;
        public int hp, maxHp, carried;
        public Vector3 position;
    }

    [Serializable] private sealed class Row
    {
        public int schema = 1;
        public string kind, session, source, revision, utc, scene, note;
        public double seconds;
        public bool playing, paused, dungeonActive, dungeonReady, server, hasCurrency, hasDungeon;
        public int day, seed, credits, litRooms, monstersAlive, corpses, spawnSlots, rank, contractProgress, contractTarget;
        public float hour, power, capacity;
        public long unityAllocatedBytes;
        public PlayerSample[] players;
    }

    [MenuItem("Tools/Gameplay QA/Begin human observation")]
    private static void BeginHuman() => Begin("human", "unspecified");

    public static string Begin(string source, string revision)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        if (IsRecording) throw new InvalidOperationException("A recording is already active.");
        if (source != "human" && source != "scripted" && source != "synthetic")
            throw new ArgumentException("Source must be human, scripted, or synthetic.");
        Directory.CreateDirectory(OutputRoot);
        _session = Guid.NewGuid().ToString("N");
        _source = source;
        _revision = string.IsNullOrWhiteSpace(revision) ? "unspecified" : revision;
        LastPath = Path.Combine(OutputRoot, DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "_" + _session + ".jsonl");
        _writer = new StreamWriter(new FileStream(LastPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        _writer.AutoFlush = true;
        _started = EditorApplication.timeSinceStartup;
        _nextSample = _started;
        _nextLookup = 0;
        Write("start", "1 Hz observer; no automatic fun verdict; monster cache refresh 5 s");
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += OnReload;
        return LastPath;
    }

    [MenuItem("Tools/Gameplay QA/Mark return-or-continue decision")]
    private static void Decision() => Mark("decision", "Observer marked a return-or-continue choice; add context in notes.");
    [MenuItem("Tools/Gameplay QA/Mark confusion")]
    private static void Confusion() => Mark("confusion", "Observer marked unclear feedback or rules.");
    [MenuItem("Tools/Gameplay QA/Mark boredom")]
    private static void Boredom() => Mark("boredom", "Observer marked waiting or repetitive activity.");
    [MenuItem("Tools/Gameplay QA/End observation")]
    private static void EndMenu() => End("observer_stop");

    public static void Mark(string kind, string note)
    {
        if (!IsRecording) throw new InvalidOperationException("No active observation.");
        if (kind != "decision" && kind != "confusion" && kind != "boredom" && kind != "note")
            throw new ArgumentException("Use decision, confusion, boredom, or note.");
        Write(kind, note);
    }

    public static void End(string reason = "observer_stop")
    {
        if (!IsRecording) return;
        try { Write("end", reason); }
        finally
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnReload;
            _writer.Dispose();
            _writer = null;
            _monsters = Array.Empty<MonsterHealth>();
            _dungeon = null;
            _currency = null;
            _spawner = null;
        }
    }

    private static void OnReload() => End("assembly_reload");
    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode) End("play_mode_exit");
    }

    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now - _started >= 3600) { End("time_limit"); return; }
        if (now < _nextSample) return;
        _nextSample = now + 1;
        try { Write("sample", null); }
        catch (Exception e)
        {
            // Do not flood the Editor if the output drive fills or the session disappears.
            try { End("recording_error"); }
            finally { Debug.LogException(e); }
        }
    }

    private static void Write(string kind, string note)
    {
        double now = EditorApplication.timeSinceStartup;
        if (now >= _nextLookup)
        {
            _nextLookup = now + 5;
            _dungeon = UnityEngine.Object.FindFirstObjectByType<NetworkDungeonController>();
            _currency = UnityEngine.Object.FindFirstObjectByType<CurrencyManager>();
            _spawner = UnityEngine.Object.FindFirstObjectByType<DungeonMonsterSpawner>();
            _monsters = UnityEngine.Object.FindObjectsByType<MonsterHealth>(FindObjectsSortMode.None);
        }
        var time = TimeManager.Active;
        var contract = DailyContractBoard.Current;
        var row = new Row
        {
            kind = kind, session = _session, source = _source, revision = _revision,
            utc = DateTime.UtcNow.ToString("O"), seconds = now - _started, note = note,
            scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
            playing = EditorApplication.isPlaying, paused = EditorApplication.isPaused,
            day = TimeManager.CurrentDay, hour = time != null ? time.GetCurrentTime() : -1,
            hasDungeon = _dungeon != null, dungeonActive = _dungeon != null && _dungeon.IsDungeonActive,
            dungeonReady = _dungeon != null && _dungeon.IsLocalDungeonReady,
            server = _dungeon != null && _dungeon.isServer,
            seed = _dungeon != null ? _dungeon.CurrentSeed : 0,
            power = _dungeon != null ? _dungeon.Power : -1, capacity = _dungeon != null ? _dungeon.PowerCapacity : -1,
            litRooms = _dungeon != null ? _dungeon.LitRoomCount : -1,
            hasCurrency = _currency != null, credits = _currency != null ? _currency.SharedCurrency : -1,
            monstersAlive = _monsters.Count(m => m != null && !m.IsDead),
            corpses = _monsters.Count(m => m != null && m.IsDead),
            spawnSlots = _spawner != null ? _spawner.AliveCount : -1,
            rank = TaxCollectionMachine.Active != null ? TaxCollectionMachine.Active.CivicRank : -1,
            contractProgress = contract != null ? contract.Progress : -1,
            contractTarget = contract != null ? contract.Target : -1,
            unityAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong(),
            players = PlayerPawn.All.Where(p => p != null && p.isActiveAndEnabled).Select(p =>
            {
                var v = p.GetComponent<PlayerVitals>();
                return new PlayerSample { id = p.owner.ToString(), owner = p.isOwner,
                    position = p.transform.position, carried = p.CarriedValue, hasVitals = v != null,
                    hp = v != null ? v.CurrentHealth : -1, maxHp = v != null ? v.MaxHealth : -1,
                    dead = v != null && v.IsDead };
            }).ToArray()
        };
        _writer.WriteLine(JsonUtility.ToJson(row));
    }
}
