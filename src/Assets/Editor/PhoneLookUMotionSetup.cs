using System.Collections.Generic;
using System.IO;
using UMotionEditor.API;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PhoneLookUMotionSetup
{
    public const string ProjectPath = "Assets/FPS/UMotion/Cyber_PhoneLook.asset";
    public const string TemplatePath = "Assets/UMotionExamples/UMotion Projects/IdleAnimation.asset";
    public const string LookClipPath =
        "Assets/RetargetedAnimations/locomotion/TakingItem/CyberGenericBaked/Cyber_Generic_Phone_Look_127_140.anim";
    public const string HoldClipPath =
        "Assets/RetargetedAnimations/locomotion/TakingItem/CyberGenericBaked/Cyber_Generic_Phone_Look_Hold.anim";
    public const string PlayerPrefabPath = "Assets/FPS/Cyber_Generic.prefab";
    public const string ExportFolder = "Assets/RetargetedAnimations/locomotion/TakingItem/CyberGenericBaked";
    public const string PreviewName = "PhoneLook_UMotionPreview";
    public const string EditScenePath = "Assets/FPS/UMotion/PhoneLook_Edit.unity";

    private static int _waitFrames;
    private static bool _setupRunning;

    [MenuItem("Tools/StillWorking/Room Power/Open UMotion Phone Look")]
    public static void OpenMenu()
    {
        Debug.Log(Open());
    }

    public static string Open()
    {
        if (EditorApplication.isPlaying)
            return "Exit Play Mode before opening UMotion Phone Look.";

        Directory.CreateDirectory("Assets/FPS/UMotion");
        EnsureEditScene();
        if (!File.Exists(ProjectPath))
        {
            if (!AssetDatabase.CopyAsset(TemplatePath, ProjectPath))
                return "Failed to copy UMotion template project.";
        }

        SetExportPath();
        ClipEditor.OpenWindow();
        PoseEditor.OpenWindow();
        _waitFrames = 0;
        if (!_setupRunning)
        {
            _setupRunning = true;
            EditorApplication.update += FinishSetupWhenReady;
        }

        return "Opening UMotion Clip Editor + Pose Editor for Cyber_Generic phone look.";
    }

    private static void FinishSetupWhenReady()
    {
        _waitFrames++;
        if (!ClipEditor.IsWindowOpened || !PoseEditor.IsWindowOpened)
        {
            if (_waitFrames > 60)
            {
                EditorApplication.update -= FinishSetupWhenReady;
                _setupRunning = false;
                Debug.LogWarning("[PhoneLookUMotion] UMotion windows did not initialize. Open Window/UMotion Editor manually, then run the menu again.");
            }

            return;
        }

        EditorApplication.update -= FinishSetupWhenReady;
        _setupRunning = false;

        ClipEditor.LoadProject(ProjectPath);
        GameObject preview = FindOrCreatePreviewCharacter();
        Selection.activeGameObject = preview;
        PoseEditor.SetAnimatedGameObject(preview);

        var clips = new List<AnimationClip>();
        AnimationClip look = AssetDatabase.LoadAssetAtPath<AnimationClip>(LookClipPath);
        AnimationClip hold = AssetDatabase.LoadAssetAtPath<AnimationClip>(HoldClipPath);
        if (look != null)
            clips.Add(look);
        if (hold != null)
            clips.Add(hold);

        if (clips.Count > 0)
            ClipEditor.ImportClips(clips, ClipEditor.ImportClipSettings.Default);

        EditorSceneManager.MarkSceneDirty(preview.scene);
        Debug.Log(
            "[PhoneLookUMotion] Ready. If a new-rig dialog appears, click Create Configuration. " +
            "Edit the right arm in Pose Mode, then File > Export. Output folder: " + ExportFolder);
    }

    private static void EnsureEditScene()
    {
        if (File.Exists(EditScenePath))
        {
            if (EditorSceneManager.GetActiveScene().path != EditScenePath)
                EditorSceneManager.OpenScene(EditScenePath, OpenSceneMode.Single);
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, EditScenePath);
    }

    private static GameObject FindOrCreatePreviewCharacter()
    {
        GameObject existing = GameObject.Find(PreviewName);
        if (existing != null)
            return existing;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (prefab == null)
            throw new FileNotFoundException(PlayerPrefabPath);

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = PreviewName;
        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;
        Undo.RegisterCreatedObjectUndo(instance, "Create PhoneLook UMotion Preview");
        return instance;
    }

    private static void SetExportPath()
    {
        Object project = AssetDatabase.LoadAssetAtPath<Object>(ProjectPath);
        if (project == null)
            return;

        var serialized = new SerializedObject(project);
        SerializedProperty export = serialized.FindProperty("Settings.ExportDestinationPath.valueInternal");
        if (export != null)
        {
            export.stringValue = ExportFolder;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(project);
        }
    }
}
