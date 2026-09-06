using System;
using System.Collections.Generic;
using System.Linq;
using Demo.Scripts.Runtime.Character;
using KINEMATION.FPSAnimationFramework.Runtime.Camera;
using KINEMATION.FPSAnimationFramework.Runtime.Layers.IkMotionLayer;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class RunningJumpAaaAuthoring
{
    public const string SourceClipPath =
        "Assets/RetargetedAnimations/locomotion/RunningJump/CyberGenericBaked/Cyber_Generic_Running_Jump_InPlace.anim";
    public const string AirClipPath =
        "Assets/RetargetedAnimations/locomotion/RunningJump/CyberGenericBaked/Cyber_Generic_Running_Jump_Air.anim";
    public const string LandClipPath =
        "Assets/RetargetedAnimations/locomotion/RunningJump/CyberGenericBaked/Cyber_Generic_Running_Jump_Land.anim";

    public const string TakeoffIkPath =
        "Assets/FPS/Cyber_Generic_FPSAnimator/IKMotion_RunningJumpTakeoff_Cyber.asset";
    public const string LandingIkPath =
        "Assets/FPS/Cyber_Generic_FPSAnimator/IKMotion_RunningJumpLand_Cyber.asset";
    public const string TakeoffCameraPath =
        "Assets/FPS/Cyber_Generic_FPSAnimator/Camera_RunningJumpTakeoff_Cyber.asset";
    public const string LandingCameraPath =
        "Assets/FPS/Cyber_Generic_FPSAnimator/Camera_RunningJumpLand_Cyber.asset";
    public const string HardLandingCameraPath =
        "Assets/FPS/Cyber_Generic_FPSAnimator/Camera_RunningJumpHardLand_Cyber.asset";

    private const string RigPath = "Assets/FPS/Cyber_Generic_FPSAnimator/Rig_Cyber_Generic.asset";
    private const string SettingsPath =
        "Assets/FPS/Cyber_Generic_FPSAnimator/Cyber_FPSControllerSettings.asset";
    private const string PlayerPrefabPath = "Assets/FPS/Cyber_Generic.prefab";

    private static readonly string[] ControllerPaths =
    {
        "Assets/FPS/Cyber_Generic_FPSAnimator/Cyber_Generic.controller",
        "Assets/FPS/Cyber_Generic_FPSAnimator/FPSAnimator_Generic.controller"
    };

    [MenuItem("Tools/StillWorking/Running Jump/Build AAA Pass")]
    public static void BuildMenu()
    {
        Debug.Log(Build());
    }

    public static void BuildBatch()
    {
        Debug.Log(Build());
    }

    public static string Build()
    {
        AnimationClip source = LoadRequired<AnimationClip>(SourceClipPath);
        AnimationClip air = BuildClipSlice(source, AirClipPath, 0f, 0.7f);
        AnimationClip land = BuildClipSlice(source, LandClipPath, 0.7f, 0.9f);

        foreach (string controllerPath in ControllerPaths)
        {
            ConfigureController(LoadRequired<AnimatorController>(controllerPath), air, land);
        }

        KRig rig = LoadRequired<KRig>(RigPath);
        KRigElement weaponBone = rig.GetElementByName("IK WeaponBone");
        if (weaponBone.index != 132)
        {
            throw new InvalidOperationException(
                $"Cyber rig IK WeaponBone index drifted: expected 132, actual {weaponBone.index}.");
        }

        IkMotionLayerSettings takeoffIk = BuildTakeoffIk(rig, weaponBone);
        IkMotionLayerSettings landingIk = BuildLandingIk(rig, weaponBone);
        FPSCameraAnimation takeoffCamera = BuildTakeoffCamera();
        FPSCameraAnimation landingCamera = BuildLandingCamera();
        FPSCameraAnimation hardLandingCamera = BuildHardLandingCamera();

        FPSControllerSettings controllerSettings = LoadRequired<FPSControllerSettings>(SettingsPath);
        controllerSettings.runningJumpTakeoffMotion = takeoffIk;
        controllerSettings.runningJumpLandingMotion = landingIk;
        controllerSettings.runningJumpTakeoffCamera = takeoffCamera;
        controllerSettings.runningJumpLandingCamera = landingCamera;
        controllerSettings.hardRunningJumpLandingCamera = hardLandingCamera;
        EditorUtility.SetDirty(controllerSettings);

        DisableAnimatorJumpLandAudioDetection();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return "Running Jump AAA assets built: Air 0.700s, Land 0.200s, Cyber IK bone 132, "
               + "three restrained camera motions, two controllers, and explicit movement-event audio.";
    }

    private static T LoadRequired<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
        {
            throw new InvalidOperationException($"Required asset is missing: {path}");
        }

        return asset;
    }

    private static AnimationClip BuildClipSlice(AnimationClip source, string path, float startTime, float endTime)
    {
        AnimationClip target = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (target == null)
        {
            target = new AnimationClip();
            AssetDatabase.CreateAsset(target, path);
        }

        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(target))
        {
            AnimationUtility.SetEditorCurve(target, binding, null);
        }

        foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(target))
        {
            AnimationUtility.SetObjectReferenceCurve(target, binding, null);
        }

        target.name = System.IO.Path.GetFileNameWithoutExtension(path);
        target.frameRate = source.frameRate;
        target.wrapMode = WrapMode.ClampForever;
        target.legacy = source.legacy;
        target.localBounds = source.localBounds;

        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
        {
            if (binding.type == typeof(Transform) && string.IsNullOrEmpty(binding.path))
            {
                continue;
            }

            AnimationCurve sourceCurve = AnimationUtility.GetEditorCurve(source, binding);
            AnimationCurve slice = SliceCurve(sourceCurve, startTime, endTime);

            if (binding.type == typeof(Animator)
                && string.IsNullOrEmpty(binding.path)
                && binding.propertyName == "SprintWeight")
            {
                slice = AnimationCurve.Constant(0f, endTime - startTime, 0f);
            }

            AnimationUtility.SetEditorCurve(target, binding, slice);
        }

        foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
        {
            ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(source, binding);
            AnimationUtility.SetObjectReferenceCurve(target, binding,
                SliceObjectCurve(keys, startTime, endTime));
        }

        AnimationEvent[] events = AnimationUtility.GetAnimationEvents(source)
            .Where(animationEvent => animationEvent.time >= startTime - 0.000001f
                                     && animationEvent.time <= endTime + 0.000001f
                                     && (startTime > 0f || animationEvent.time < endTime - 0.000001f))
            .Select(animationEvent => CloneEvent(animationEvent, animationEvent.time - startTime))
            .ToArray();
        AnimationUtility.SetAnimationEvents(target, events);

        AnimationClipSettings clipSettings = AnimationUtility.GetAnimationClipSettings(source);
        clipSettings.loopTime = false;
        clipSettings.loopBlend = false;
        clipSettings.loopBlendOrientation = false;
        clipSettings.loopBlendPositionY = false;
        clipSettings.loopBlendPositionXZ = false;
        AnimationUtility.SetAnimationClipSettings(target, clipSettings);

        EditorUtility.SetDirty(target);
        return target;
    }

    private static AnimationCurve SliceCurve(AnimationCurve source, float startTime, float endTime)
    {
        float length = endTime - startTime;
        var keys = new List<Keyframe> { CreateBoundaryKey(source, startTime, 0f) };

        foreach (Keyframe sourceKey in source.keys)
        {
            if (sourceKey.time <= startTime + 0.000001f || sourceKey.time >= endTime - 0.000001f)
            {
                continue;
            }

            Keyframe shifted = sourceKey;
            shifted.time -= startTime;
            keys.Add(shifted);
        }

        keys.Add(CreateBoundaryKey(source, endTime, length));

        var result = new AnimationCurve(keys.ToArray())
        {
            preWrapMode = source.preWrapMode,
            postWrapMode = source.postWrapMode
        };
        return result;
    }

    private static Keyframe CreateBoundaryKey(AnimationCurve source, float sourceTime, float targetTime)
    {
        foreach (Keyframe sourceKey in source.keys)
        {
            if (Mathf.Abs(sourceKey.time - sourceTime) <= 0.000001f)
            {
                Keyframe exact = sourceKey;
                exact.time = targetTime;
                return exact;
            }
        }

        const float sampleStep = 0.0001f;
        float left = source.Evaluate(Mathf.Max(source.keys[0].time, sourceTime - sampleStep));
        float right = source.Evaluate(Mathf.Min(source.keys[^1].time, sourceTime + sampleStep));
        float span = Mathf.Max(0.000001f,
            Mathf.Min(source.keys[^1].time, sourceTime + sampleStep)
            - Mathf.Max(source.keys[0].time, sourceTime - sampleStep));
        float tangent = (right - left) / span;
        return new Keyframe(targetTime, source.Evaluate(sourceTime), tangent, tangent);
    }

    private static ObjectReferenceKeyframe[] SliceObjectCurve(
        ObjectReferenceKeyframe[] source, float startTime, float endTime)
    {
        if (source == null || source.Length == 0)
        {
            return Array.Empty<ObjectReferenceKeyframe>();
        }

        Object ValueAt(float time)
        {
            Object value = source[0].value;
            foreach (ObjectReferenceKeyframe key in source)
            {
                if (key.time > time + 0.000001f) break;
                value = key.value;
            }
            return value;
        }

        var result = new List<ObjectReferenceKeyframe>
        {
            new() { time = 0f, value = ValueAt(startTime) }
        };

        foreach (ObjectReferenceKeyframe key in source)
        {
            if (key.time <= startTime + 0.000001f || key.time >= endTime - 0.000001f) continue;
            result.Add(new ObjectReferenceKeyframe { time = key.time - startTime, value = key.value });
        }

        result.Add(new ObjectReferenceKeyframe { time = endTime - startTime, value = ValueAt(endTime) });
        return result.ToArray();
    }

    private static AnimationEvent CloneEvent(AnimationEvent source, float targetTime)
    {
        return new AnimationEvent
        {
            time = Mathf.Max(0f, targetTime),
            functionName = source.functionName,
            stringParameter = source.stringParameter,
            floatParameter = source.floatParameter,
            intParameter = source.intParameter,
            objectReferenceParameter = source.objectReferenceParameter,
            messageOptions = source.messageOptions
        };
    }

    private static void ConfigureController(AnimatorController controller, AnimationClip air, AnimationClip land)
    {
        EnsureParameter(controller, "RunningJump", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "RunningJumpAirSpeed", AnimatorControllerParameterType.Float);
        EnsureParameter(controller, "RunningJumpLand", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine inAir = controller.layers.Single(layer => layer.name == "InAir").stateMachine;
        AnimatorState empty = FindState(inAir, "Empty");
        AnimatorState jumpStart = FindState(inAir, "JumpStart");
        AnimatorState jumpLoop = FindState(inAir, "JumpLoop");
        AnimatorState jumpEnd = FindState(inAir, "JumpEnd");
        AnimatorState runningAir = FindStateOptional(inAir, "RunningJumpAir")
                                   ?? FindStateOptional(inAir, "RunningJump")
                                   ?? inAir.AddState("RunningJumpAir", new Vector3(520f, 70f));
        runningAir.name = "RunningJumpAir";
        runningAir.motion = air;
        runningAir.speed = 1f;
        runningAir.speedParameter = "RunningJumpAirSpeed";
        runningAir.speedParameterActive = true;
        runningAir.writeDefaultValues = true;

        AnimatorState runningLand = FindStateOptional(inAir, "RunningJumpLand")
                                    ?? inAir.AddState("RunningJumpLand", new Vector3(730f, 70f));
        runningLand.motion = land;
        runningLand.speed = 1f;
        runningLand.speedParameterActive = false;
        runningLand.writeDefaultValues = true;

        RemoveTransitionsTo(empty, runningAir);
        RemoveTransitionsTo(jumpEnd, runningAir);
        RemoveTransitionsTo(jumpLoop, runningLand);
        RemoveAllTransitions(runningAir);
        RemoveAllTransitions(runningLand);

        AddFixedTransition(empty, runningAir, 0.06f, false, 0f,
            ("InAir", AnimatorConditionMode.If), ("RunningJump", AnimatorConditionMode.If));
        AddFixedTransition(jumpEnd, runningAir, 0.06f, false, 0f,
            ("InAir", AnimatorConditionMode.If), ("RunningJump", AnimatorConditionMode.If));

        AddFixedTransition(runningAir, runningLand, 0.04f, false, 0f,
            ("RunningJumpLand", AnimatorConditionMode.If));
        AddFixedTransition(runningAir, jumpLoop, 0.05f, true, 1.02f,
            ("InAir", AnimatorConditionMode.If));

        PrependFixedTransition(jumpLoop, runningLand, 0.04f, false, 0f,
            ("RunningJumpLand", AnimatorConditionMode.If));
        AddFixedTransition(runningLand, runningAir, 0.04f, false, 0f,
            ("InAir", AnimatorConditionMode.If), ("RunningJump", AnimatorConditionMode.If));
        AddFixedTransition(runningLand, jumpStart, 0.04f, false, 0f,
            ("InAir", AnimatorConditionMode.If), ("RunningJump", AnimatorConditionMode.IfNot));
        AddFixedTransition(runningLand, empty, 0.06f, true, 1f);

        EditorUtility.SetDirty(inAir);
        EditorUtility.SetDirty(controller);
    }

    private static void EnsureParameter(
        AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        AnimatorControllerParameter parameter = controller.parameters.FirstOrDefault(candidate => candidate.name == name);
        if (parameter == null)
        {
            controller.AddParameter(name, type);
            return;
        }

        if (parameter.type != type)
        {
            throw new InvalidOperationException(
                $"Animator parameter '{name}' has type {parameter.type}, expected {type}: {controller.name}");
        }
    }

    private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
    {
        return FindStateOptional(stateMachine, stateName)
               ?? throw new InvalidOperationException($"State '{stateName}' is missing in {stateMachine.name}.");
    }

    private static AnimatorState FindStateOptional(AnimatorStateMachine stateMachine, string stateName)
    {
        return stateMachine.states.FirstOrDefault(child => child.state.name == stateName).state;
    }

    private static void RemoveTransitionsTo(AnimatorState source, AnimatorState destination)
    {
        foreach (AnimatorStateTransition transition in source.transitions
                     .Where(candidate => candidate.destinationState == destination).ToArray())
        {
            source.RemoveTransition(transition);
        }
    }

    private static void RemoveAllTransitions(AnimatorState source)
    {
        foreach (AnimatorStateTransition transition in source.transitions.ToArray())
        {
            source.RemoveTransition(transition);
        }
    }

    private static AnimatorStateTransition AddFixedTransition(
        AnimatorState source,
        AnimatorState destination,
        float duration,
        bool hasExitTime,
        float exitTime,
        params (string parameter, AnimatorConditionMode mode)[] conditions)
    {
        AnimatorStateTransition transition = source.AddTransition(destination);
        ConfigureTransition(transition, duration, hasExitTime, exitTime, conditions);
        return transition;
    }

    private static void PrependFixedTransition(
        AnimatorState source,
        AnimatorState destination,
        float duration,
        bool hasExitTime,
        float exitTime,
        params (string parameter, AnimatorConditionMode mode)[] conditions)
    {
        AnimatorStateTransition[] previous = source.transitions;
        AnimatorStateTransition transition = source.AddTransition(destination);
        ConfigureTransition(transition, duration, hasExitTime, exitTime, conditions);
        source.transitions = new[] { transition }.Concat(previous).ToArray();
    }

    private static void ConfigureTransition(
        AnimatorStateTransition transition,
        float duration,
        bool hasExitTime,
        float exitTime,
        params (string parameter, AnimatorConditionMode mode)[] conditions)
    {
        transition.hasFixedDuration = true;
        transition.duration = duration;
        transition.hasExitTime = hasExitTime;
        transition.exitTime = exitTime;
        transition.offset = 0f;
        transition.interruptionSource = TransitionInterruptionSource.None;
        transition.orderedInterruption = true;
        transition.canTransitionToSelf = false;

        foreach ((string parameter, AnimatorConditionMode mode) in conditions)
        {
            transition.AddCondition(mode, 0f, parameter);
        }
    }

    private static IkMotionLayerSettings BuildTakeoffIk(KRig rig, KRigElement weaponBone)
    {
        IkMotionLayerSettings asset = GetOrCreate<IkMotionLayerSettings>(TakeoffIkPath);
        ConfigureIkBase(asset, rig, weaponBone);
        asset.rotationCurves = new VectorCurve
        {
            x = SmoothCurve((0f, 0f), (0.14f, 4f), (0.36f, -1f), (0.60f, 0f), (1f, 0f)),
            y = ZeroCurve(1f),
            z = SmoothCurve((0f, 0f), (0.14f, 5.5f), (0.36f, -1.5f), (0.60f, 0f), (1f, 0f))
        };
        asset.translationCurves = new VectorCurve
        {
            x = ZeroCurve(1f),
            y = SmoothCurve((0f, 0f), (0.14f, -0.018f), (0.36f, -0.005f), (0.60f, 0f), (1f, 0f)),
            z = SmoothCurve((0f, 0f), (0.14f, -0.008f), (0.36f, 0f), (0.60f, 0f), (1f, 0f))
        };
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static IkMotionLayerSettings BuildLandingIk(KRig rig, KRigElement weaponBone)
    {
        IkMotionLayerSettings asset = GetOrCreate<IkMotionLayerSettings>(LandingIkPath);
        ConfigureIkBase(asset, rig, weaponBone);
        asset.rotationCurves = new VectorCurve
        {
            x = SmoothCurve((0f, 0f), (0.10f, 2f), (0.30f, -0.5f), (0.55f, 0f), (1f, 0f)),
            y = ZeroCurve(1f),
            z = SmoothCurve((0f, 0f), (0.10f, 0.8f), (0.30f, -0.2f), (0.55f, 0f), (1f, 0f))
        };
        asset.translationCurves = new VectorCurve
        {
            x = ZeroCurve(1f),
            y = SmoothCurve((0f, 0f), (0.10f, -0.0245f), (0.30f, 0.005f), (0.55f, 0f), (1f, 0f)),
            z = SmoothCurve((0f, 0f), (0.10f, 0.005f), (0.30f, 0f), (0.55f, 0f), (1f, 0f))
        };
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static void ConfigureIkBase(IkMotionLayerSettings asset, KRig rig, KRigElement weaponBone)
    {
        asset.rigAsset = rig;
        asset.alpha = 1f;
        asset.boneToAnimate = weaponBone;
        asset.rotationScale = Vector3.one;
        asset.translationScale = Vector3.one;
        asset.blendTime = 0.04f;
        asset.playRate = 2f;
        asset.autoBlendOut = true;
        asset.linkDynamically = false;
        asset.curveBlending.Clear();
    }

    private static FPSCameraAnimation BuildTakeoffCamera()
    {
        FPSCameraAnimation asset = GetOrCreate<FPSCameraAnimation>(TakeoffCameraPath);
        ConfigureCamera(asset,
            SmoothCurve((0f, 0f), (0.06f, -0.010f), (0.16f, 0.012f), (0.30f, 0f)),
            SmoothCurve((0f, 0f), (0.08f, 0.35f), (0.18f, -0.20f), (0.30f, 0f)),
            0.30f);
        return asset;
    }

    private static FPSCameraAnimation BuildLandingCamera()
    {
        FPSCameraAnimation asset = GetOrCreate<FPSCameraAnimation>(LandingCameraPath);
        ConfigureCamera(asset,
            SmoothCurve((0f, 0f), (0.04f, -0.025f), (0.12f, 0.006f), (0.24f, 0f)),
            SmoothCurve((0f, 0f), (0.05f, 0.65f), (0.12f, -0.15f), (0.24f, 0f)),
            0.24f);
        return asset;
    }

    private static FPSCameraAnimation BuildHardLandingCamera()
    {
        FPSCameraAnimation asset = GetOrCreate<FPSCameraAnimation>(HardLandingCameraPath);
        ConfigureCamera(asset,
            SmoothCurve((0f, 0f), (0.05f, -0.035f), (0.16f, 0.008f), (0.32f, 0f)),
            SmoothCurve((0f, 0f), (0.06f, 1.1f), (0.17f, -0.25f), (0.32f, 0f)),
            0.32f);
        return asset;
    }

    private static void ConfigureCamera(
        FPSCameraAnimation asset, AnimationCurve vertical, AnimationCurve pitch, float length)
    {
        asset.translation = new VectorCurve
        {
            x = ZeroCurve(length),
            y = vertical,
            z = ZeroCurve(length)
        };
        asset.rotation = new VectorCurve
        {
            x = pitch,
            y = ZeroCurve(length),
            z = ZeroCurve(length)
        };
        asset.blendTime = new BlendTime(0.04f, 0.04f);
        asset.scale = 1f;
        EditorUtility.SetDirty(asset);
    }

    private static AnimationCurve SmoothCurve(params (float time, float value)[] samples)
    {
        var curve = new AnimationCurve(samples.Select(sample => new Keyframe(sample.time, sample.value)).ToArray());
        for (int i = 0; i < curve.length; i++)
        {
            curve.SmoothTangents(i, 0f);
        }
        return curve;
    }

    private static AnimationCurve ZeroCurve(float length)
    {
        return AnimationCurve.Constant(0f, length, 0f);
    }

    private static T GetOrCreate<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;

        asset = ScriptableObject.CreateInstance<T>();
        asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static void DisableAnimatorJumpLandAudioDetection()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            PlayerSound sound = root.GetComponentInChildren<PlayerSound>(true);
            if (sound == null)
            {
                throw new InvalidOperationException($"PlayerSound is missing from {PlayerPrefabPath}.");
            }

            var serializedSound = new SerializedObject(sound);
            SerializedProperty autoDetect = serializedSound.FindProperty("autoDetectJumpLand");
            if (autoDetect == null)
            {
                throw new InvalidOperationException("PlayerSound.autoDetectJumpLand could not be serialized.");
            }

            autoDetect.boolValue = false;
            serializedSound.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
