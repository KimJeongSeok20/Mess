using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StillWorking.EditorAudio
{
    /// <summary>
    /// Authoring-time matching for clips with the same purpose. Boost is opt-in and capped at 6 dB.
    /// Active RMS is a 20 ms gated sample measurement, not LUFS or perceived loudness.
    /// </summary>
    public static class ClipBalanceProcessor
    {
        private const string OutputRoot = "Assets/Sfx/ClipBalanced/";

        private sealed class Plan
        {
            public AudioClip source;
            public ClipBalanceEntry entry;
            public string path;
            public string sourceHash;
            public float[] samples;
            public ClipBalanceMetrics metrics;
            public float gainDb;
            public AudioClip output;
        }

        public static ClipBalanceMetrics Measure(AudioClip clip)
        {
            return MeasureSamples(ReadSamples(clip), clip.channels, clip.frequency);
        }

        public static float CalculateGain(ClipBalanceMetrics metrics, float target, float ceiling)
        {
            return CalculateGain(metrics, target, ceiling, 0f);
        }

        public static float CalculateGain(ClipBalanceMetrics metrics, float target, float ceiling, float maxBoostDb)
        {
            if (metrics == null || !Finite(metrics.peakDb) || !Finite(metrics.activeRmsDb))
                throw new InvalidOperationException("Clip metrics must be finite.");
            ValidateLevel(target, "Target active RMS");
            ValidateLevel(ceiling, "Peak ceiling");
            ValidateBoost(maxBoostDb);
            return Mathf.Min(maxBoostDb, target - metrics.activeRmsDb, ceiling - metrics.peakDb);
        }

        public static string AnalyzeGroup(ClipBalanceGroup group)
        {
            ValidateGroup(group);
            var report = Header(group);
            foreach (var clip in group.clips.Distinct())
            {
                try
                {
                    var metrics = Measure(clip);
                    AppendMetrics(report, clip.name, metrics,
                        CalculateGain(metrics, group.targetActiveRmsDb, group.peakCeilingDb, group.maxBoostDb));
                }
                catch (Exception exception)
                {
                    report.AppendLine($"ERROR | {(clip ? clip.name : "Missing clip")} | {exception.Message}");
                }
            }
            return report.ToString();
        }

        public static string ApplyGroup(ClipBalanceCatalog catalog, ClipBalanceGroup group)
        {
            ValidateCatalog(catalog, group);
            ValidateBindings(group.bindingAssets, catalog);
            var plans = BuildPlans(catalog, group);
            var report = Header(group);

            // Complete every decode, hash and binding check before touching output assets.
            foreach (var plan in plans)
            {
                WriteWave(plan.path, plan.samples, plan.metrics.channels, plan.metrics.sampleRate, plan.gainDb);
                AssetDatabase.ImportAsset(plan.path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(plan.path) as AudioImporter;
                if (!importer)
                    throw new InvalidOperationException($"Audio importer missing: {plan.path}");
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                importer.forceToMono = false;
                importer.loadInBackground = false;
                var importerSettings = new SerializedObject(importer);
                var normalize = importerSettings.FindProperty("m_Normalize") ?? importerSettings.FindProperty("normalize");
                if (normalize == null)
                    throw new InvalidOperationException($"Audio normalize setting missing: {plan.path}");
                normalize.boolValue = false;
                importerSettings.ApplyModifiedPropertiesWithoutUndo();
                // These are new PCM assets; playback loop/pitch remains on the original AudioSource.
                importer.SaveAndReimport();
                plan.output = AssetDatabase.LoadAssetAtPath<AudioClip>(plan.path);
                if (!plan.output || plan.output.channels != plan.metrics.channels ||
                    plan.output.frequency != plan.metrics.sampleRate)
                    throw new InvalidOperationException($"Imported format differs from source: {plan.path}");

                // Record ownership before rebinding so a later binding failure is safe to retry.
                if (plan.entry == null)
                {
                    plan.entry = new ClipBalanceEntry { source = plan.source, group = group.name };
                    catalog.entries.Add(plan.entry);
                }
                plan.entry.output = plan.output;
                plan.entry.gainDb = plan.gainDb;
                plan.entry.sourceHash = plan.sourceHash;
                plan.entry.outputHash = HashFile(plan.path);
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
                AppendMetrics(report, plan.source.name, plan.metrics, plan.gainDb);
            }

            var replacements = plans.ToDictionary(plan => plan.source, plan => plan.output);
            var changed = ReplaceBindings(group.bindingAssets, replacements);
            // Keeping original inputs makes subsequent analysis and application idempotent.
            for (var index = 0; index < group.clips.Count; index++)
                group.clips[index] = ResolveSource(catalog, group.clips[index]);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            report.AppendLine($"Applied {plans.Count} clips; replaced {changed} clip references in explicit binding assets.");
            report.AppendLine("Source files retained. Use Restore to rebind the original clips.");
            return report.ToString();
        }

        public static string RestoreGroup(ClipBalanceCatalog catalog, ClipBalanceGroup group)
        {
            ValidateCatalog(catalog, group);
            ValidateBindings(group.bindingAssets, catalog);
            var entries = catalog.entries.Where(entry => entry != null && entry.group == group.name).ToList();
            var replacements = new Dictionary<AudioClip, AudioClip>();
            foreach (var entry in entries)
            {
                ValidateEntry(entry, ExpectedOutputPath(group, entry.source));
                if (replacements.ContainsKey(entry.output))
                    throw new InvalidOperationException("Duplicate output mapping in catalog.");
                replacements.Add(entry.output, entry.source);
            }
            var changed = ReplaceBindings(group.bindingAssets, replacements);
            for (var index = 0; index < group.clips.Count; index++)
                group.clips[index] = ResolveSource(catalog, group.clips[index]);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            return $"Restored {changed} clip references in {group.name}. Generated clips and hash records were retained for reapplication.";
        }

        /// <summary>CPU-only regression checks. Creates no Unity objects or project assets.</summary>
        public static string RunSyntheticRegression()
        {
            var tone = new float[1000];
            for (var index = 0; index < tone.Length; index++)
                tone[index] = (float)(0.25 * Math.Sin(2 * Math.PI * 50 * index / 1000));
            var mono = MeasureSamples(tone, 1, 1000);
            var padded = new float[2000];
            Array.Copy(tone, padded, tone.Length);
            var withSilence = MeasureSamples(padded, 1, 1000);
            AssertNear(mono.activeRmsDb, withSilence.activeRmsDb, "Silence padding must not dilute active RMS");
            var dynamicTone = new float[1000];
            for (var index = 0; index < dynamicTone.Length; index++)
                dynamicTone[index] = (float)((index < 100 ? 0.1 : 0.002) * Math.Sin(2 * Math.PI * 50 * index / 1000));
            var scaledTone = dynamicTone.Select(sample => sample * 0.1f).ToArray();
            AssertNear(MeasureSamples(scaledTone, 1, 1000).activeRmsDb,
                MeasureSamples(dynamicTone, 1, 1000).activeRmsDb - 20f, "Active RMS must scale consistently with gain");

            var stereo = new float[tone.Length * 2];
            for (var index = 0; index < tone.Length; index++)
                stereo[index * 2] = stereo[index * 2 + 1] = tone[index];
            var twoChannels = MeasureSamples(stereo, 2, 1000);
            AssertNear(mono.activeRmsDb, twoChannels.activeRmsDb, "Identical stereo channels must not add 3 dB");
            AssertNear(mono.duration, twoChannels.duration, "Interleaved channel duration");
            AssertNear(CalculateGain(mono, mono.activeRmsDb - 6f, 0), -6f, "RMS attenuation");
            AssertNear(CalculateGain(mono, 0, mono.peakDb - 3f), -3f, "Peak ceiling priority");
            AssertNear(CalculateGain(mono, 0, 0), 0, "Default must not boost");
            AssertNear(CalculateGain(mono, 0, 0, 6), 6, "Explicit boost cap");
            AssertNear(CalculateGain(mono, 0, mono.peakDb + 2f, 6), 2, "Boost still obeys peak ceiling");
            var rejected = false;
            try { MeasureSamples(new float[100], 1, 1000); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected)
                throw new InvalidOperationException("Silent clip regression failed.");
            rejected = false;
            try { CalculateGain(mono, -24, -6, float.NaN); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected)
                throw new InvalidOperationException("Non-finite setting regression failed.");
            return "PASS: 11 synthetic checks (silence gate, gain scaling, stereo energy/duration, attenuation, peak ceiling, no default boost, opt-in boost cap, boost ceiling, silence rejection, non-finite rejection).";
        }

        private static void AssertNear(float actual, float expected, string label)
        {
            if (Mathf.Abs(actual - expected) > 0.001f)
                throw new InvalidOperationException($"{label}: {actual} differs from {expected}.");
        }

        private static List<Plan> BuildPlans(ClipBalanceCatalog catalog, ClipBalanceGroup group)
        {
            var plans = new List<Plan>();
            var seen = new HashSet<AudioClip>();
            foreach (var clip in group.clips)
            {
                var source = ResolveSource(catalog, clip);
                if (!source)
                    throw new InvalidOperationException($"{group.name} contains a missing clip.");
                if (!seen.Add(source))
                    continue;
                var sourcePath = AssetDatabase.GetAssetPath(source);
                if (!sourcePath.StartsWith("Assets/", StringComparison.Ordinal) ||
                    sourcePath.StartsWith(OutputRoot, StringComparison.OrdinalIgnoreCase) || !AssetDatabase.IsMainAsset(source))
                    throw new InvalidOperationException($"Use a persistent original clip under Assets: {source.name}");
                var path = ExpectedOutputPath(group, source);
                if (plans.Any(plan => string.Equals(plan.path, path, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Multiple sources resolve to the same output: {path}");
                var entries = catalog.entries.Where(entry => entry != null && entry.group == group.name && entry.source == source).ToList();
                if (entries.Count > 1)
                    throw new InvalidOperationException($"Duplicate source mapping: {source.name}");
                var entry = entries.SingleOrDefault();
                if (entry != null)
                    ValidateEntry(entry, path);
                else if (File.Exists(AbsolutePath(path)) || AssetDatabase.LoadMainAssetAtPath(path))
                    throw new InvalidOperationException($"Untracked output already exists; refusing to overwrite: {path}");

                var samples = ReadSamples(source);
                var metrics = MeasureSamples(samples, source.channels, source.frequency);
                plans.Add(new Plan
                {
                    source = source, entry = entry, path = path, sourceHash = HashFile(sourcePath),
                    samples = samples, metrics = metrics,
                    gainDb = CalculateGain(metrics, group.targetActiveRmsDb, group.peakCeilingDb, group.maxBoostDb)
                });
            }
            return plans;
        }

        private static AudioClip ResolveSource(ClipBalanceCatalog catalog, AudioClip clip)
        {
            if (!clip)
                return null;
            var entries = catalog.entries.Where(entry => entry != null && entry.output == clip).ToList();
            if (entries.Count > 1)
                throw new InvalidOperationException($"Ambiguous output mapping: {clip.name}");
            if (entries.Count == 0)
                return clip;
            if (!entries[0].source)
                throw new InvalidOperationException($"Original clip missing for {clip.name}");
            return entries[0].source;
        }

        private static void ValidateEntry(ClipBalanceEntry entry, string expectedPath)
        {
            if (!entry.source || !entry.output || string.IsNullOrEmpty(entry.sourceHash) || string.IsNullOrEmpty(entry.outputHash))
                throw new InvalidOperationException("A recorded source/output mapping is missing or incomplete.");
            if (AssetDatabase.GetAssetPath(entry.output) != expectedPath)
                throw new InvalidOperationException($"Output path changed for {entry.source.name}; refusing to overwrite.");
            if (HashFile(AssetDatabase.GetAssetPath(entry.source)) != entry.sourceHash)
                throw new InvalidOperationException($"Original audio changed since the last application: {entry.source.name}");
            if (HashFile(expectedPath) != entry.outputHash)
                throw new InvalidOperationException($"Generated audio changed outside this tool: {expectedPath}");
        }

        private static void ValidateCatalog(ClipBalanceCatalog catalog, ClipBalanceGroup group)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before applying or restoring clip balance.");
            if (!catalog || !AssetDatabase.Contains(catalog) || !AssetDatabase.GetAssetPath(catalog).StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("Save the catalog under Assets before applying.");
            ValidateGroup(group);
            if (catalog.groups == null || !catalog.groups.Contains(group) || catalog.entries == null)
                throw new InvalidOperationException("The group must belong to this catalog.");
            var orphan = catalog.entries.FirstOrDefault(entry => entry != null &&
                !catalog.groups.Any(existing => existing != null && existing.name == entry.group));
            if (orphan != null)
                throw new InvalidOperationException($"Restore the recorded group name '{orphan.group}' in the catalog. Applied group names identify their saved outputs and cannot be renamed or removed.");
            foreach (var clip in group.clips)
            {
                var source = ResolveSource(catalog, clip);
                if (catalog.groups.Any(other => other != null && other != group && other.clips != null &&
                    other.clips.Any(candidate => ResolveSource(catalog, candidate) == source)))
                    throw new InvalidOperationException($"Assign each source to one balance group: {source?.name}. Shared clips cannot use two different corrections in the same binding assets.");
            }
            if (catalog.groups.Any(other => other != group && other != null && string.Equals(Sanitize(other.name), Sanitize(group.name), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Group names must produce unique output folder names.");
        }

        private static void ValidateGroup(ClipBalanceGroup group)
        {
            if (group == null || string.IsNullOrWhiteSpace(group.name) || Sanitize(group.name).Length == 0)
                throw new InvalidOperationException("Enter a group name.");
            if (group.clips == null || group.clips.Count == 0)
                throw new InvalidOperationException($"{group.name} has no clips.");
            ValidateLevel(group.targetActiveRmsDb, "Target active RMS");
            ValidateLevel(group.peakCeilingDb, "Peak ceiling");
            ValidateBoost(group.maxBoostDb);
        }

        private static void ValidateBindings(List<Object> assets, ClipBalanceCatalog catalog)
        {
            if (assets == null)
                throw new InvalidOperationException("Binding asset list is missing.");
            foreach (var asset in assets.Distinct())
            {
                var path = asset ? AssetDatabase.GetAssetPath(asset) : string.Empty;
                if (!asset || !path.StartsWith("Assets/", StringComparison.Ordinal) || asset == catalog)
                    throw new InvalidOperationException("Bindings must be explicit, saved project assets other than the catalog.");
                if (asset is ScriptableObject)
                    continue;
                if (asset is GameObject && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                    PrefabUtility.IsPartOfPrefabAsset(asset) && PrefabUtility.GetPrefabAssetType(asset) != PrefabAssetType.Model)
                    continue;
                throw new InvalidOperationException($"Unsupported binding {path}. Only ScriptableObjects and editable prefabs are supported; scenes are excluded.");
            }
        }

        private static int ReplaceBindings(List<Object> assets, Dictionary<AudioClip, AudioClip> replacements)
        {
            var count = 0;
            foreach (var asset in assets.Distinct())
            {
                if (asset is ScriptableObject)
                {
                    var changed = ReplaceReferences(asset, replacements);
                    if (changed > 0)
                    {
                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssetIfDirty(asset);
                    }
                    count += changed;
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(asset);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var changed = 0;
                    foreach (var component in root.GetComponentsInChildren<Component>(true))
                    {
                        if (component)
                            changed += ReplaceReferences(component, replacements);
                    }
                    if (changed > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path, out var success);
                        if (!success)
                            throw new InvalidOperationException($"Failed to save prefab: {path}");
                    }
                    count += changed;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            return count;
        }

        private static int ReplaceReferences(Object target, Dictionary<AudioClip, AudioClip> replacements)
        {
            var serialized = new SerializedObject(target);
            var iterator = serialized.GetIterator();
            var count = 0;
            while (iterator.Next(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ObjectReference ||
                    !(iterator.objectReferenceValue is AudioClip clip) || !replacements.TryGetValue(clip, out var replacement) || clip == replacement)
                    continue;
                iterator.objectReferenceValue = replacement;
                count++;
            }
            if (count > 0)
                serialized.ApplyModifiedPropertiesWithoutUndo();
            return count;
        }

        private static float[] ReadSamples(AudioClip clip)
        {
            if (!clip)
                throw new InvalidOperationException("Audio clip is missing.");
            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
            if (clip.loadState != AudioDataLoadState.Loaded)
                throw new InvalidOperationException($"{clip.name}: sample data is {clip.loadState}; retry analysis when audio loading completes.");
            if (clip.loadType != AudioClipLoadType.DecompressOnLoad)
                throw new InvalidOperationException($"{clip.name}: GetData requires Decompress On Load; the original importer is unchanged.");
            var sampleCount = (long)clip.samples * clip.channels;
            if (sampleCount <= 0 || sampleCount > int.MaxValue || clip.frequency <= 0)
                throw new InvalidOperationException($"{clip.name}: invalid or unsupported sample count.");
            var samples = new float[(int)sampleCount];
            if (!clip.GetData(samples, 0))
                throw new InvalidOperationException($"{clip.name}: Unity could not decode samples; the original importer is unchanged.");
            return samples;
        }

        private static ClipBalanceMetrics MeasureSamples(float[] samples, int channels, int sampleRate)
        {
            var blockSize = checked(Math.Max(1, (int)Math.Round(sampleRate * 0.02)) * channels);
            var energy = new double[(samples.Length + blockSize - 1) / blockSize];
            double peak = 0, loudestRms = 0;
            for (var block = 0; block < energy.Length; block++)
            {
                var end = Math.Min(samples.Length, (block + 1) * blockSize);
                for (var index = block * blockSize; index < end; index++)
                {
                    var value = samples[index];
                    if (!Finite(value))
                        throw new InvalidOperationException("Decoded samples contain a non-finite value.");
                    peak = Math.Max(peak, Math.Abs(value));
                    energy[block] += (double)value * value;
                }
                loudestRms = Math.Max(loudestRms, Math.Sqrt(energy[block] / (end - block * blockSize)));
            }
            if (loudestRms < 0.001)
                throw new InvalidOperationException("The loudest 20 ms block is below -60 dBFS; this near-silent clip requires manual review.");
            // A relative gate keeps the active block set unchanged when applying gain.
            var gate = loudestRms * Math.Pow(10, -35.0 / 20);
            double activeEnergy = 0;
            long activeSamples = 0;
            for (var block = 0; block < energy.Length; block++)
            {
                var length = Math.Min(blockSize, samples.Length - block * blockSize);
                if (Math.Sqrt(energy[block] / length) < gate)
                    continue;
                activeEnergy += energy[block];
                activeSamples += length;
            }
            if (activeSamples == 0 || peak == 0)
                throw new InvalidOperationException("Clip has no active blocks; no gain will be applied.");
            return new ClipBalanceMetrics
            {
                peakDb = Db(peak), activeRmsDb = Db(Math.Sqrt(activeEnergy / activeSamples)),
                duration = samples.Length / (float)channels / sampleRate, channels = channels, sampleRate = sampleRate
            };
        }

        private static void WriteWave(string path, float[] samples, int channels, int sampleRate, float gainDb)
        {
            var absolute = AbsolutePath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            var gain = Math.Pow(10, gainDb / 20.0);
            var bytes = checked(samples.Length * 2);
            using (var writer = new BinaryWriter(File.Open(absolute, FileMode.Create, FileAccess.Write, FileShare.None)))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(checked(36 + bytes));
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)channels); writer.Write(sampleRate);
                writer.Write(checked(sampleRate * channels * 2)); writer.Write((short)(channels * 2)); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
                foreach (var sample in samples)
                    writer.Write((short)Math.Round(Math.Max(-1, Math.Min(1, sample * gain)) * 32767));
            }
        }

        private static string ExpectedOutputPath(ClipBalanceGroup group, AudioClip source)
        {
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            if (string.IsNullOrEmpty(guid))
                throw new InvalidOperationException("Original clip has no persistent asset GUID.");
            return OutputRoot + Sanitize(group.name) + "/" + guid + ".wav";
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return new string(value.Trim().Select(character => char.IsLetterOrDigit(character) || character == '-' || character == '_' ? character : '_').ToArray());
        }

        private static string AbsolutePath(string assetPath)
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var absolute = Path.GetFullPath(Path.Combine(root, assetPath));
            if (!absolute.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Asset path escapes the project.");
            return absolute;
        }

        private static string HashFile(string path)
        {
            using (var stream = File.OpenRead(AbsolutePath(path)))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void ValidateLevel(float value, string label)
        {
            if (!Finite(value) || value < -120 || value > 0)
                throw new InvalidOperationException($"{label} must be finite and between -120 and 0 dBFS.");
        }

        private static void ValidateBoost(float value)
        {
            if (!Finite(value) || value < 0 || value > 6)
                throw new InvalidOperationException("Maximum boost must be finite and between 0 and 6 dB. Zero keeps attenuation-only behavior.");
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Db(double amplitude) => (float)(20 * Math.Log10(Math.Max(1e-12, amplitude)));

        private static StringBuilder Header(ClipBalanceGroup group)
        {
            return new StringBuilder().AppendLine($"{group.name} | target active RMS {group.targetActiveRmsDb:F2} dBFS | sample peak ceiling {group.peakCeilingDb:F2} dBFS | maximum boost {group.maxBoostDb:F2} dB")
                .AppendLine("20 ms active RMS, relative gate loudest block -35 dB; reject loudest blocks below -60 dBFS. This is not LUFS.")
                .AppendLine("Clip | peak | active RMS | applied gain | estimated result RMS (dBFS)");
        }

        private static void AppendMetrics(StringBuilder report, string name, ClipBalanceMetrics metrics, float gain)
        {
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} | {1:F2} | {2:F2} | {3:F2} dB | {4:F2}",
                name, metrics.peakDb, metrics.activeRmsDb, gain, metrics.activeRmsDb + gain));
        }
    }
}
