using System;
using System.Collections.Generic;
using UnityEngine;

namespace StillWorking.EditorAudio
{
    [Serializable]
    public sealed class ClipBalanceGroup
    {
        public string name;
        [Range(-80f, 0f)] public float targetActiveRmsDb = -24f;
        [Range(-80f, 0f)] public float peakCeilingDb = -6f;
        [Range(0f, 6f)] public float maxBoostDb;
        public List<AudioClip> clips = new List<AudioClip>();
        public List<UnityEngine.Object> bindingAssets = new List<UnityEngine.Object>();
    }

    [Serializable]
    public sealed class ClipBalanceEntry
    {
        public AudioClip source;
        public AudioClip output;
        public string group;
        public float gainDb;
        public string sourceHash;
        public string outputHash;
    }

    [CreateAssetMenu(menuName = "Audio/Clip Balance Catalog", fileName = "ClipBalanceCatalog")]
    public sealed class ClipBalanceCatalog : ScriptableObject
    {
        public List<ClipBalanceGroup> groups = new List<ClipBalanceGroup>();
        public List<ClipBalanceEntry> entries = new List<ClipBalanceEntry>();
    }

    public sealed class ClipBalanceMetrics
    {
        public float peakDb;
        public float activeRmsDb;
        public float duration;
        public int channels;
        public int sampleRate;
    }
}
