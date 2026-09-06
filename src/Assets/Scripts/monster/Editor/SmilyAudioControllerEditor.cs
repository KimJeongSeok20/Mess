using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SmilyAudioController))]
public sealed class SmilyAudioControllerEditor : Editor
{
    private delegate void RuntimePreview(SmilyAudioController controller);

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawProfileSection();
        SmilyAudioController.MonsterAudioProfile profile = GetProfile();
        DrawSourceSection();
        DrawVolumeSection(profile);
        DrawClipSections(profile);

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(8f);
        DrawPreviewSection(profile);
    }

    private void DrawProfileSection()
    {
        SerializedProperty profileProperty = serializedObject.FindProperty("profile");
        if (profileProperty == null)
            return;

        EditorGUILayout.PropertyField(profileProperty, new GUIContent("SFX Profile"));
        SmilyAudioController.MonsterAudioProfile profile = (SmilyAudioController.MonsterAudioProfile)profileProperty.enumValueIndex;
        string helpText = profile switch
        {
            SmilyAudioController.MonsterAudioProfile.Smily => "Shows the full Smily event set, including warning smile, jump land, and flee start.",
            SmilyAudioController.MonsterAudioProfile.Clown => "Shows only the Clown event set. Smily-only slots are hidden but existing serialized values are preserved.",
            _ => "Shows every event slot for debugging or new monster setup."
        };

        EditorGUILayout.HelpBox(helpText, MessageType.None);
    }

    private SmilyAudioController.MonsterAudioProfile GetProfile()
    {
        SerializedProperty profileProperty = serializedObject.FindProperty("profile");
        return profileProperty != null
            ? (SmilyAudioController.MonsterAudioProfile)profileProperty.enumValueIndex
            : SmilyAudioController.MonsterAudioProfile.Custom;
    }

    private void DrawSourceSection()
    {
        DrawPropertyGroup(
            "Source",
            "audioSource",
            "loopSource",
            "oneShotSource",
            "volume",
            "loopVolume",
            "minDistance",
            "maxDistance");
    }

    private void DrawVolumeSection(SmilyAudioController.MonsterAudioProfile profile)
    {
        DrawVisiblePropertyGroup(
            "Event Volumes",
            profile,
            "idleLoopVolume",
            "patrolLoopVolume",
            "detectVolume",
            "warningSmileVolume",
            "teleportWindupVolume",
            "teleportVolume",
            "attackWindupVolume",
            "attackCommitVolume",
            "jumpLandVolume",
            "fleeStartVolume",
            "doorOpenVolume",
            "deathVolume",
            "goreExplosionVolume");
    }

    private void DrawClipSections(SmilyAudioController.MonsterAudioProfile profile)
    {
        DrawVisiblePropertyGroup(
            "Movement Loops",
            profile,
            "idleLoopClips",
            "patrolLoopClips");

        DrawVisiblePropertyGroup(
            "Awareness",
            profile,
            "detectClips",
            "warningSmileClips",
            "teleportWindupClips",
            "teleportClips");

        DrawVisiblePropertyGroup(
            "Attack",
            profile,
            "attackWindupClips",
            "attackCommitClips",
            "jumpAttackClips",
            "jumpLandClips",
            "fleeStartClips",
            "doorOpenClips");

        DrawVisiblePropertyGroup(
            "Death",
            profile,
            "deathClips",
            "goreExplosionClips");
    }

    private void DrawPropertyGroup(string label, params string[] propertyNames)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        for (int i = 0; i < propertyNames.Length; i++)
            DrawProperty(propertyNames[i]);
    }

    private void DrawVisiblePropertyGroup(string label, SmilyAudioController.MonsterAudioProfile profile, params string[] propertyNames)
    {
        if (!AnyVisible(profile, propertyNames))
            return;

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        for (int i = 0; i < propertyNames.Length; i++)
        {
            if (IsVisibleForProfile(profile, propertyNames[i]))
                DrawProperty(propertyNames[i]);
        }
    }

    private void DrawProperty(string propertyName)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            EditorGUILayout.PropertyField(property, true);
    }

    private static bool AnyVisible(SmilyAudioController.MonsterAudioProfile profile, string[] propertyNames)
    {
        for (int i = 0; i < propertyNames.Length; i++)
        {
            if (IsVisibleForProfile(profile, propertyNames[i]))
                return true;
        }

        return false;
    }

    private static bool IsVisibleForProfile(SmilyAudioController.MonsterAudioProfile profile, string propertyName)
    {
        if (profile == SmilyAudioController.MonsterAudioProfile.Custom)
            return true;

        bool smilyOnly = propertyName is
            "warningSmileVolume" or
            "warningSmileClips" or
            "jumpAttackClips" or
            "jumpLandVolume" or
            "jumpLandClips" or
            "fleeStartVolume" or
            "fleeStartClips";

        bool clownOnly = propertyName is
            "teleportWindupVolume" or
            "teleportWindupClips";

        if (smilyOnly)
            return profile == SmilyAudioController.MonsterAudioProfile.Smily;

        if (clownOnly)
            return profile == SmilyAudioController.MonsterAudioProfile.Clown;

        return true;
    }

    private void DrawPreviewSection(SmilyAudioController.MonsterAudioProfile profile)
    {
        SmilyAudioController controller = (SmilyAudioController)target;

        EditorGUILayout.LabelField("Monster Audio Preview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            Application.isPlaying
                ? "Play Mode: buttons call the monster audio runtime methods, so event volume is applied."
                : "Edit Mode: buttons use Unity's editor clip preview. Effective volume is shown for balancing, but editor preview output may ignore volume scaling.",
            MessageType.Info);

        DrawPreviewRowIfVisible(profile, "Idle Loop", "idleLoopClips", "idleLoopVolume", true, controller => controller.PlayIdleLoop());
        DrawPreviewRowIfVisible(profile, "Patrol Loop", "patrolLoopClips", "patrolLoopVolume", true, controller => controller.PlayPatrolLoop());
        DrawPreviewRowIfVisible(profile, "Detect", "detectClips", "detectVolume", false, controller => controller.PlayDetect());
        DrawPreviewRowIfVisible(profile, "Warning Smile", "warningSmileClips", "warningSmileVolume", false, controller => controller.PlayWarningSmile());
        DrawPreviewRowIfVisible(profile, "Teleport Windup", "teleportWindupClips", "teleportWindupVolume", false, controller => controller.PlayTeleportWindup());
        DrawPreviewRowIfVisible(profile, "Teleport", "teleportClips", "teleportVolume", false, controller => controller.PlayTeleport());
        DrawPreviewRowIfVisible(profile, "Attack Windup", "attackWindupClips", "attackWindupVolume", false, controller => controller.PlayAttackWindup());
        DrawPreviewRowIfVisible(profile, "Attack Commit", "attackCommitClips", "attackCommitVolume", false, controller => controller.PlayAttackCommit());
        DrawPreviewRowIfVisible(profile, "Jump Attack Legacy", "jumpAttackClips", "attackCommitVolume", false, controller => controller.PlayJumpAttack());
        DrawPreviewRowIfVisible(profile, "Jump Land", "jumpLandClips", "jumpLandVolume", false, controller => controller.PlayJumpLand());
        DrawPreviewRowIfVisible(profile, "Flee Start", "fleeStartClips", "fleeStartVolume", false, controller => controller.PlayFleeStart());
        DrawPreviewRowIfVisible(profile, "Door Open", "doorOpenClips", "doorOpenVolume", false, controller => controller.PlayDoorOpen());
        DrawPreviewRowIfVisible(profile, "Death", "deathClips", "deathVolume", false, controller => controller.PlayDeath());
        DrawPreviewRowIfVisible(profile, "Gore Explosion", "goreExplosionClips", "goreExplosionVolume", false, controller => controller.PlayGoreExplosion());

        EditorGUILayout.Space(4f);
        if (GUILayout.Button("Stop Preview"))
            StopPreview(controller);
    }

    private void DrawPreviewRowIfVisible(
        SmilyAudioController.MonsterAudioProfile profile,
        string label,
        string clipsPropertyName,
        string volumePropertyName,
        bool loop,
        RuntimePreview runtimePreview)
    {
        if (IsVisibleForProfile(profile, clipsPropertyName))
            DrawPreviewRow(label, clipsPropertyName, volumePropertyName, loop, runtimePreview);
    }

    private void DrawPreviewRow(string label, string clipsPropertyName, string volumePropertyName, bool loop, RuntimePreview runtimePreview)
    {
        SerializedProperty clipsProperty = serializedObject.FindProperty(clipsPropertyName);
        AudioClip clip = PickRandomClip(clipsProperty);
        bool hasClip = clip != null;
        float effectiveVolume = GetEffectiveVolume(volumePropertyName, loop);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, GUILayout.Width(132f));
            EditorGUILayout.LabelField($"Vol {effectiveVolume:0.00}", GUILayout.Width(70f));

            using (new EditorGUI.DisabledScope(!hasClip && !Application.isPlaying))
            {
                string buttonLabel = Application.isPlaying ? "Play Runtime" : loop ? "Preview Loop" : "Preview";
                if (GUILayout.Button(buttonLabel, GUILayout.Width(104f)))
                {
                    if (Application.isPlaying)
                    {
                        serializedObject.ApplyModifiedProperties();
                        runtimePreview((SmilyAudioController)target);
                    }
                    else
                    {
                        PlayEditorPreview(clip, loop);
                    }
                }
            }

            EditorGUILayout.LabelField(hasClip ? clip.name : "No clips", EditorStyles.miniLabel);
        }
    }

    private float GetEffectiveVolume(string eventVolumePropertyName, bool loop)
    {
        float masterVolume = GetFloat("volume", 1f);
        float eventVolume = GetFloat(eventVolumePropertyName, 1f);
        float loopMasterVolume = loop ? GetFloat("loopVolume", 1f) : 1f;
        return Mathf.Clamp01(masterVolume * loopMasterVolume * eventVolume);
    }

    private float GetFloat(string propertyName, float fallback)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        return property != null ? property.floatValue : fallback;
    }

    private static AudioClip PickRandomClip(SerializedProperty clipsProperty)
    {
        if (clipsProperty == null || !clipsProperty.isArray || clipsProperty.arraySize == 0)
            return null;

        List<AudioClip> clips = new List<AudioClip>();
        for (int i = 0; i < clipsProperty.arraySize; i++)
        {
            AudioClip clip = clipsProperty.GetArrayElementAtIndex(i).objectReferenceValue as AudioClip;
            if (clip != null)
                clips.Add(clip);
        }

        if (clips.Count == 0)
            return null;

        return clips[UnityEngine.Random.Range(0, clips.Count)];
    }

    private static void StopPreview(SmilyAudioController controller)
    {
        if (Application.isPlaying)
        {
            controller.StopMovementLoop("InspectorPreviewStop");
            AudioSource[] sources = controller.GetComponents<AudioSource>();
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] != null)
                    sources[i].Stop();
            }

            return;
        }

        StopEditorPreview();
    }

    private static void PlayEditorPreview(AudioClip clip, bool loop)
    {
        if (clip == null)
            return;

        StopEditorPreview();

        Type audioUtilType = GetAudioUtilType();
        if (audioUtilType == null)
        {
            Debug.LogWarning("[SmilyAudioControllerEditor] UnityEditor.AudioUtil was not found; edit-mode preview is unavailable.");
            return;
        }

        MethodInfo playPreviewClip = audioUtilType.GetMethod(
            "PlayPreviewClip",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(AudioClip), typeof(int), typeof(bool) },
            null);

        if (InvokeAudioUtil(playPreviewClip, clip, 0, loop))
            return;

        MethodInfo playClip = audioUtilType.GetMethod(
            "PlayClip",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(AudioClip), typeof(int), typeof(bool) },
            null);

        if (InvokeAudioUtil(playClip, clip, 0, loop))
            return;

        playClip = audioUtilType.GetMethod(
            "PlayClip",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(AudioClip) },
            null);

        if (!InvokeAudioUtil(playClip, clip))
            Debug.LogWarning("[SmilyAudioControllerEditor] No compatible AudioUtil preview method was found.");
    }

    private static void StopEditorPreview()
    {
        Type audioUtilType = GetAudioUtilType();
        if (audioUtilType == null)
            return;

        MethodInfo stopAllPreviewClips = audioUtilType.GetMethod(
            "StopAllPreviewClips",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);

        if (InvokeAudioUtil(stopAllPreviewClips))
            return;

        MethodInfo stopAllClips = audioUtilType.GetMethod(
            "StopAllClips",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);

        InvokeAudioUtil(stopAllClips);
    }

    private static Type GetAudioUtilType()
    {
        return typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
    }

    private static bool InvokeAudioUtil(MethodInfo method, params object[] args)
    {
        if (method == null)
            return false;

        try
        {
            method.Invoke(null, args);
            return true;
        }
        catch (TargetInvocationException exception)
        {
            Debug.LogWarning($"[SmilyAudioControllerEditor] Audio preview failed: {exception.InnerException?.Message ?? exception.Message}");
            return false;
        }
    }
}
