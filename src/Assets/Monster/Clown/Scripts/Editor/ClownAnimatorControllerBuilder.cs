using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ClownAnimatorControllerBuilder
{
    private const string ControllerPath = "Assets/Monster/Clown/ClownAnimator.controller";
    private const string ClownPrefabPath = "Assets/Clown/Prefab/Clown skin2 combined.prefab";
    private const string ClownTestScenePath = "Assets/Clown/Scenes/ClownTest.unity";

    private const string IdlePath = "Assets/Clown/Animation/Male Locomotion Pack/idle.fbx";
    private const string WalkPath = "Assets/Clown/Animation/Male Locomotion Pack/walking.fbx";
    private const string SourceModelPath = "Assets/Clown/Animation/Male Locomotion Pack/Clown.fbx";
    private const string ThrowPath = "Assets/Clown/Animation/Throw.fbx";
    private const string DisappearPath = "Assets/Clown/Animation/Disappearing.fbx";

    private const string IdleClipName = "Clown_Idle";
    private const string WalkClipName = "Clown_Walk";
    private const string ThrowClipName = "Clown_Throw";
    private const string DisappearClipName = "Clown_Disappear";

    private const string MoveSpeedParameter = "MoveSpeed";
    private const string ThrowTrigger = "Throw";
    private const string RepeatThrowParameter = "RepeatThrow";

    public static string BuildAndAssign()
    {
        EnsureSourceAvatar();
        Avatar sourceAvatar = LoadSourceAvatar();

        ConfigureClip(IdlePath, IdleClipName, true);
        ConfigureClip(WalkPath, WalkClipName, true);
        ConfigureClip(ThrowPath, ThrowClipName, false);
        ConfigureClip(DisappearPath, DisappearClipName, false);

        ApplySourceAvatar(IdlePath, sourceAvatar, true);
        ApplySourceAvatar(WalkPath, sourceAvatar, true);
        ApplySourceAvatar(ThrowPath, sourceAvatar, false);
        ApplySourceAvatar(DisappearPath, sourceAvatar, false);

        AnimationClip idleClip = LoadClip(IdlePath, IdleClipName);
        AnimationClip walkClip = LoadClip(WalkPath, WalkClipName);
        AnimationClip throwClip = LoadClip(ThrowPath, ThrowClipName);
        AnimationClip disappearClip = LoadClip(DisappearPath, DisappearClipName);

        if (idleClip == null || walkClip == null || throwClip == null || disappearClip == null)
            return "Missing one or more clown animation clips";

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter(MoveSpeedParameter, AnimatorControllerParameterType.Float);
        controller.AddParameter(ThrowTrigger, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(RepeatThrowParameter, AnimatorControllerParameterType.Bool);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        stateMachine.anyStatePosition = new Vector3(-300f, -20f, 0f);

        BlendTree locomotionTree = new BlendTree
        {
            name = "Locomotion",
            blendType = BlendTreeType.Simple1D,
            blendParameter = MoveSpeedParameter,
            useAutomaticThresholds = false
        };
        AssetDatabase.AddObjectToAsset(locomotionTree, controller);

        AnimatorState locomotionState = stateMachine.AddState("Locomotion", new Vector3(120f, 40f, 0f));
        locomotionState.motion = locomotionTree;
        locomotionTree.blendType = BlendTreeType.Simple1D;
        locomotionTree.blendParameter = MoveSpeedParameter;
        locomotionTree.useAutomaticThresholds = false;
        locomotionTree.AddChild(idleClip, 0f);
        locomotionTree.AddChild(walkClip, 1f);
        stateMachine.defaultState = locomotionState;

        AnimatorState throwState = stateMachine.AddState("Throw", new Vector3(420f, -40f, 0f));
        throwState.motion = throwClip;

        AnimatorState disappearState = stateMachine.AddState("Disappear", new Vector3(980f, -40f, 0f));
        disappearState.motion = disappearClip;

        AnimatorStateTransition throwTransition = stateMachine.AddAnyStateTransition(throwState);
        throwTransition.hasExitTime = false;
        throwTransition.hasFixedDuration = true;
        throwTransition.duration = 0.05f;
        throwTransition.canTransitionToSelf = false;
        throwTransition.AddCondition(AnimatorConditionMode.If, 0f, ThrowTrigger);

        AddConditionalExitTransition(throwState, locomotionState, AnimatorConditionMode.If, RepeatThrowParameter);
        AddConditionalExitTransition(throwState, disappearState, AnimatorConditionMode.IfNot, RepeatThrowParameter);
        AddExitTransition(disappearState, locomotionState);

        AssetDatabase.SaveAssets();

        AssignControllerToPrefab(controller);
        AssignControllerToScene(controller);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return "Built and assigned clown animator controller";
    }

    private static void ConfigureClip(string assetPath, string clipName, bool loop)
    {
        if (!(AssetImporter.GetAtPath(assetPath) is ModelImporter importer))
            return;

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;

        if (clips == null || clips.Length == 0)
            return;

        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].name = clipName;
            clips[i].loopTime = loop;
            clips[i].loopPose = loop;
            clips[i].keepOriginalPositionY = true;
            clips[i].keepOriginalPositionXZ = true;
            clips[i].keepOriginalOrientation = true;
            clips[i].lockRootHeightY = true;
        }

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    private static void EnsureSourceAvatar()
    {
        if (!(AssetImporter.GetAtPath(SourceModelPath) is ModelImporter importer))
            return;

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.SaveAndReimport();
    }

    private static Avatar LoadSourceAvatar()
    {
        return AssetDatabase.LoadAllAssetsAtPath(SourceModelPath).OfType<Avatar>().FirstOrDefault();
    }

    private static void ApplySourceAvatar(string assetPath, Avatar sourceAvatar, bool loop)
    {
        if (sourceAvatar == null)
            return;

        if (!(AssetImporter.GetAtPath(assetPath) is ModelImporter importer))
            return;

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = sourceAvatar;

        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;

        if (clips != null)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].loopTime = loop;
                clips[i].loopPose = loop;
                clips[i].keepOriginalPositionY = true;
                clips[i].keepOriginalPositionXZ = true;
                clips[i].keepOriginalOrientation = true;
                clips[i].lockRootHeightY = true;
            }

            importer.clipAnimations = clips;
        }

        importer.SaveAndReimport();
    }

    private static AnimationClip LoadClip(string assetPath, string clipName)
    {
        return AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().FirstOrDefault(clip => clip.name == clipName);
    }

    private static void AddExitTransition(AnimatorState from, AnimatorState to)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = 0.98f;
        transition.hasFixedDuration = true;
        transition.duration = 0.05f;
    }

    private static void AddConditionalExitTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode conditionMode, string conditionParameter)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = 0.98f;
        transition.hasFixedDuration = true;
        transition.duration = 0.05f;
        transition.AddCondition(conditionMode, 0f, conditionParameter);
    }

    private static void AssignControllerToPrefab(RuntimeAnimatorController controller)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ClownPrefabPath);
        try
        {
            Animator animator = root.GetComponent<Animator>();
            if (animator == null)
                return;

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            EditorUtility.SetDirty(animator);
            PrefabUtility.SaveAsPrefabAsset(root, ClownPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void AssignControllerToScene(RuntimeAnimatorController controller)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ClownTestScenePath) == null)
            return;

        var scene = EditorSceneManager.OpenScene(ClownTestScenePath, OpenSceneMode.Single);
        GameObject clown = GameObject.Find("ClownSkin2_Test");
        if (clown == null)
            return;

        Animator animator = clown.GetComponent<Animator>();
        if (animator == null)
            return;

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        EditorUtility.SetDirty(animator);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
