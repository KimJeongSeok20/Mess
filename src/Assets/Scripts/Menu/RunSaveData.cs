using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class RunSaveData
{
    public const int CurrentVersion = 1;
    public int version = CurrentVersion;
    public string savedUtc;
    public int day = 1, currency, unlockedCells, civicRank = 1, skillPointsEarned, skillPoints;
    public int health = 100;
    public float stamina = 100f;
    public int dueThroughCycle, paidThroughCycle;
    public float pendingDiscount;
    public RunContractSave contract = new();
    public RunInventorySave[] inventory = Array.Empty<RunInventorySave>();
    public string skillWebName;
    public RunSkillSave[] skills = Array.Empty<RunSkillSave>();

    public bool Validate(out string error)
    {
        error = "Save data is invalid or belongs to an unsupported version.";
        if (version != CurrentVersion || !DateTime.TryParse(savedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out _)
            || day < 1 || day > 10000 || currency < 0 || unlockedCells < 0 || unlockedCells > 32
            || civicRank < 1 || skillPointsEarned < 0 || skillPoints < 0
            || health <= 0 || health > 100000 || float.IsNaN(stamina) || float.IsInfinity(stamina) || stamina < 0 || stamina > 100000
            || paidThroughCycle < 0 || dueThroughCycle < paidThroughCycle || dueThroughCycle > day
            || float.IsNaN(pendingDiscount) || pendingDiscount < 0 || pendingDiscount > 0.5f
            || inventory == null || inventory.Length > 36 || skills == null || skills.Length > 512
            || contract == null || !contract.IsValid(day)) return false;
        var tokens = new HashSet<string>();
        var slots = new HashSet<int>();
        foreach (var entry in inventory)
        {
            if (entry == null || entry.slot < 0 || entry.slot >= 4 + unlockedCells || !slots.Add(entry.slot)
                || string.IsNullOrWhiteSpace(entry.receipt.token) || !tokens.Add(entry.receipt.token)
                || string.IsNullOrWhiteSpace(entry.receipt.itemName) || entry.receipt.price < 0
                || entry.receipt.rarity < -1 || entry.receipt.rarity > 4 || entry.receipt.upgradeTier < 0
                || entry.receipt.ammo < 0) return false;
        }
        var nodes = new HashSet<int>();
        foreach (var skill in skills)
            if (skill == null || skill.level < 0 || skill.level > 1000 || !nodes.Add(skill.nodeId)
                || string.IsNullOrEmpty(skill.guid)) return false;
        if (skills.Length > 0 && string.IsNullOrEmpty(skillWebName)) return false;
        error = null;
        return true;
    }
}

[Serializable]
public sealed class RunInventorySave { public int slot; public InventoryReceipt receipt; }
[Serializable]
public sealed class RunSkillSave { public int nodeId, level; public string guid; }
[Serializable]
public sealed class RunContractSave
{
    public int templateIndex = -1, target, progress, day, state, lastTemplateIndex = -1;
    public bool IsValid(int runDay) => templateIndex >= 0 && templateIndex < 5 && lastTemplateIndex >= -1
        && lastTemplateIndex < 5 && target > 0 && progress >= 0 && progress <= target && day == runDay
        && (state == 1 || state == 2) && (state != 2 || progress == target);
}

/// <summary>Versioned local checkpoint file. A failed write leaves the previous checkpoint intact.</summary>
public static class RunSaveStorage
{
    [Serializable] private sealed class Envelope { public string payload, sha256; }
    private const long MaxFileBytes = 1024 * 1024;

    public static bool TryRead(string path, out RunSaveData data, out string error)
    {
        data = null;
        error = "No morning checkpoint has been saved yet.";
        try
        {
            if (!File.Exists(path)) return false;
            if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException("Checkpoint exceeds the size limit.");
            var envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(path, Encoding.UTF8));
            if (envelope == null || string.IsNullOrEmpty(envelope.payload)
                || !string.Equals(Hash(envelope.payload), envelope.sha256, StringComparison.Ordinal))
                throw new InvalidDataException("Checkpoint is incomplete or corrupt.");
            var loaded = JsonUtility.FromJson<RunSaveData>(envelope.payload);
            if (loaded == null || !loaded.Validate(out error)) return false;
            data = loaded;
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is ArgumentException)
        { error = "Could not read checkpoint: " + exception.Message; return false; }
    }

    public static bool TryWrite(string path, RunSaveData data, out string error)
    {
        if (data == null) { error = "No checkpoint data was provided."; return false; }
        if (!data.Validate(out error)) return false;
        string temporary = path + ".tmp";
        try
        {
            string payload = JsonUtility.ToJson(data);
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Envelope { payload = payload, sha256 = Hash(payload) }));
            if (bytes.Length > MaxFileBytes) throw new InvalidDataException("Checkpoint exceeds the size limit.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (!TryRead(temporary, out _, out error)) return false;
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException)
        { error = "Could not save checkpoint: " + exception.Message; return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string Hash(string value)
    {
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }
}
