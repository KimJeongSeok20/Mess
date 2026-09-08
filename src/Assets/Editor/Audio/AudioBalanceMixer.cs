using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace StillWorking.EditorAudio
{
    // Unity's public AudioMixer.SetFloat only changes a runtime override. Authoring
    // must edit the saved snapshot, using the same Editor API as its group fader.
    public static class AudioBalanceMixer
    {
        public const string MixerPath = "Assets/SoundController.mixer";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static float GetVolume(string groupName)
        {
            var mixer = LoadMixer();
            var group = FindGroup(mixer, groupName);
            var snapshot = GetStartSnapshot(mixer);
            var getter = group.GetType().GetMethod("GetValueForVolume", Flags)
                ?? throw new NotSupportedException("This Unity Editor does not expose its mixer volume authoring API.");
            return (float)getter.Invoke(group, new object[] { mixer, snapshot });
        }

        public static void SetVolume(string groupName, float decibels)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before saving mixer levels.");
            if (float.IsNaN(decibels) || float.IsInfinity(decibels) || decibels < -60f || decibels > 0f)
                throw new ArgumentOutOfRangeException(nameof(decibels), "Use -60 to 0 dB in Audio Balance.");

            var mixer = LoadMixer();
            var group = FindGroup(mixer, groupName);
            var snapshot = GetStartSnapshot(mixer);
            var setter = group.GetType().GetMethod("SetValueForVolume", Flags)
                ?? throw new NotSupportedException("This Unity Editor does not expose its mixer volume authoring API.");
            Undo.RecordObjects(new UnityEngine.Object[] { mixer, snapshot }, "Set audio group level");
            setter.Invoke(group, new object[] { mixer, snapshot, decibels });
            EditorUtility.SetDirty(snapshot);
            EditorUtility.SetDirty(mixer);
            AssetDatabase.SaveAssetIfDirty(mixer);
        }

        private static AudioMixer LoadMixer() => AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath)
            ?? throw new InvalidOperationException("SoundController mixer is missing.");

        private static AudioMixerGroup FindGroup(AudioMixer mixer, string name) =>
            mixer.FindMatchingGroups("").SingleOrDefault(group => group.name == name)
            ?? throw new InvalidOperationException("Mixer group not found: " + name);

        private static AudioMixerSnapshot GetStartSnapshot(AudioMixer mixer)
        {
            var property = mixer.GetType().GetProperty("startSnapshot", Flags)
                ?? throw new NotSupportedException("This Unity Editor does not expose its starting mixer snapshot.");
            return property.GetValue(mixer) as AudioMixerSnapshot
                ?? throw new InvalidOperationException("Mixer has no starting snapshot.");
        }
    }
}
