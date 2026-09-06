using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class FistJumpAuthoringTool
{
    private const string ControllerPath = "Assets/FPS/Cyber_Generic_FPSAnimator/Cyber_Generic.controller";
    private const string FistOverridePath = "Assets/FPS/Weapon/Fist v/FPSAnimator_Unarmed_Generic.overrideController";

    private const string GenericJumpStartPath = "Assets/RetargetedAnimations/locomotion/InAir/Cyber_Retarget_C_JumpStart.anim";
    private const string GenericJumpLoopPath = "Assets/RetargetedAnimations/locomotion/InAir/Cyber_Retarget_C_JumpLoop.anim";
    private const string GenericJumpEndPath = "Assets/RetargetedAnimations/locomotion/InAir/Cyber_Retarget_C_JumpEnd.anim";
    private const string FistIdlePath = "Assets/RetargetedAnimations/Fist/Cyber_Retarget_Fists_Idle.anim";

    private const string FistJumpStartPath = "Assets/RetargetedAnimations/Fist/Cyber_Retarget_Fists_JumpStart.anim";
    private const string FistJumpLoopPath = "Assets/RetargetedAnimations/Fist/Cyber_Retarget_Fists_JumpLoop.anim";
    private const string FistJumpEndPath = "Assets/RetargetedAnimations/Fist/Cyber_Retarget_Fists_JumpEnd.anim";

    private const float DefaultJumpStartBlendStart = 0.9f;
    private const float DefaultJumpStartBlendEnd = 1f;

    private const string JumpStartStateName = "JumpStart";
    private const string JumpLoopStateName = "JumpLoop";
    private const string JumpEndStateName = "JumpEnd";

    // Keep lower body / lower torso from the base jump and only enforce a fist pose from upper chest upward.
    private const string UpperBodyRootPath = "root/pelvis/spine_01/spine_02/spine_03/spine_04";

    [MenuItem("Tools/Fist/Generate Sprint Jump Fix")]
    public static void GenerateSprintJumpFix()
    {
        GenerateSprintJumpFix(DefaultJumpStartBlendStart, DefaultJumpStartBlendEnd);
    }

    public static void GenerateSprintJumpFix(float jumpStartBlendStart, float jumpStartBlendEnd = DefaultJumpStartBlendEnd)
    {
        var genericJumpStart = LoadClip(GenericJumpStartPath);
        var genericJumpLoop = LoadClip(GenericJumpLoopPath);
        var genericJumpEnd = LoadClip(GenericJumpEndPath);
        var fistIdle = LoadClip(FistIdlePath);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            throw new InvalidOperationException($"Missing controller at '{ControllerPath}'.");
        }

        var fistOverride = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(FistOverridePath);
        if (fistOverride == null)
        {
            throw new InvalidOperationException($"Missing override controller at '{FistOverridePath}'.");
        }

        var fistJumpStart = CreateOrUpdateDerivedClip(genericJumpStart, fistIdle, FistJumpStartPath, jumpStartBlendStart, jumpStartBlendEnd);
        var fistJumpLoop = CreateOrUpdateDerivedClip(genericJumpLoop, fistIdle, FistJumpLoopPath, 1f, 1f);
        var fistJumpEnd = CreateOrUpdateDerivedClip(genericJumpEnd, fistIdle, FistJumpEndPath, 1f, 1f);

        EnsureDistinctJumpStateSources(controller, genericJumpStart, genericJumpLoop, genericJumpEnd);
        ApplyFistJumpOverrides(fistOverride, genericJumpStart, genericJumpLoop, genericJumpEnd, fistJumpStart, fistJumpLoop, fistJumpEnd);

        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(fistOverride);
        EditorUtility.SetDirty(fistJumpStart);
        EditorUtility.SetDirty(fistJumpLoop);
        EditorUtility.SetDirty(fistJumpEnd);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "[FistJumpAuthoringTool] Generated fist jump clips and applied jump overrides: " +
            $"start='{fistJumpStart.name}', loop='{fistJumpLoop.name}', end='{fistJumpEnd.name}', jumpStartBlendStart={jumpStartBlendStart:F3}, jumpStartBlendEnd={jumpStartBlendEnd:F3}.");
    }

    private static AnimationClip LoadClip(string assetPath)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
        if (clip == null)
        {
            throw new InvalidOperationException($"Missing animation clip at '{assetPath}'.");
        }

        return clip;
    }

    private static AnimationClip CreateOrUpdateDerivedClip(AnimationClip baseClip, AnimationClip poseClip, string outputPath, float blendStart, float blendEnd)
    {
        EnsureParentDirectoryExists(outputPath);

        var targetClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
        if (targetClip == null)
        {
            targetClip = new AnimationClip
            {
                name = Path.GetFileNameWithoutExtension(outputPath)
            };

            AssetDatabase.CreateAsset(targetClip, outputPath);
        }

        EditorUtility.CopySerialized(baseClip, targetClip);
        targetClip.name = Path.GetFileNameWithoutExtension(outputPath);

        var upperBodyPaths = CollectUpperBodyPaths(baseClip, poseClip);
        foreach (var path in upperBodyPaths)
        {
            if (!TryGetQuaternionCurves(baseClip, path, out var baseCurves) ||
                !TryGetQuaternionCurves(poseClip, path, out var poseCurves))
            {
                continue;
            }

            var bakedCurves = BakeBlendedQuaternionCurves(baseClip.length, baseCurves, poseCurves, blendStart, blendEnd);
            SetQuaternionCurves(targetClip, path, bakedCurves);
        }

        EditorUtility.SetDirty(targetClip);
        return targetClip;
    }

    private static void EnsureParentDirectoryExists(string assetPath)
    {
        var fullPath = Path.GetFullPath(assetPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static List<string> CollectUpperBodyPaths(AnimationClip baseClip, AnimationClip poseClip)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);

        AddPathsFromClip(baseClip, paths);
        AddPathsFromClip(poseClip, paths);

        return paths.OrderBy(path => path, StringComparer.Ordinal).ToList();
    }

    private static void AddPathsFromClip(AnimationClip clip, ISet<string> paths)
    {
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (!binding.propertyName.StartsWith("m_LocalRotation", StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsUpperBodyPath(binding.path))
            {
                continue;
            }

            paths.Add(binding.path);
        }
    }

    private static bool IsUpperBodyPath(string path)
    {
        return string.Equals(path, UpperBodyRootPath, StringComparison.Ordinal) ||
               path.StartsWith(UpperBodyRootPath + "/", StringComparison.Ordinal);
    }

    private static bool TryGetQuaternionCurves(AnimationClip clip, string path, out QuaternionCurves curves)
    {
        curves = default;

        var x = AnimationUtility.GetEditorCurve(clip, CreateRotationBinding(path, "m_LocalRotation.x"));
        var y = AnimationUtility.GetEditorCurve(clip, CreateRotationBinding(path, "m_LocalRotation.y"));
        var z = AnimationUtility.GetEditorCurve(clip, CreateRotationBinding(path, "m_LocalRotation.z"));
        var w = AnimationUtility.GetEditorCurve(clip, CreateRotationBinding(path, "m_LocalRotation.w"));

        if (x == null || y == null || z == null || w == null)
        {
            return false;
        }

        curves = new QuaternionCurves(x, y, z, w);
        return true;
    }

    private static EditorCurveBinding CreateRotationBinding(string path, string propertyName)
    {
        return EditorCurveBinding.FloatCurve(path, typeof(Transform), propertyName);
    }

    private static QuaternionCurves BakeBlendedQuaternionCurves(
        float duration,
        QuaternionCurves baseCurves,
        QuaternionCurves poseCurves,
        float blendStart,
        float blendEnd)
    {
        var times = new SortedSet<float>();
        AddCurveTimes(baseCurves.X, times);
        AddCurveTimes(baseCurves.Y, times);
        AddCurveTimes(baseCurves.Z, times);
        AddCurveTimes(baseCurves.W, times);

        if (times.Count == 0)
        {
            times.Add(0f);
            times.Add(Mathf.Max(0.01f, duration));
        }
        else
        {
            times.Add(0f);
            times.Add(duration);
        }

        var xKeys = new List<Keyframe>(times.Count);
        var yKeys = new List<Keyframe>(times.Count);
        var zKeys = new List<Keyframe>(times.Count);
        var wKeys = new List<Keyframe>(times.Count);

        var poseQuaternion = Normalize(EvaluateQuaternion(poseCurves, 0f));
        Quaternion? previous = null;

        foreach (var time in times)
        {
            var baseQuaternion = Normalize(EvaluateQuaternion(baseCurves, time));
            var t = duration > 0f ? Mathf.Clamp01(time / duration) : 1f;
            var blend = Mathf.Lerp(blendStart, blendEnd, t);
            var bakedQuaternion = Normalize(Quaternion.Slerp(baseQuaternion, poseQuaternion, blend));

            if (previous.HasValue && Quaternion.Dot(previous.Value, bakedQuaternion) < 0f)
            {
                bakedQuaternion = new Quaternion(-bakedQuaternion.x, -bakedQuaternion.y, -bakedQuaternion.z, -bakedQuaternion.w);
            }

            previous = bakedQuaternion;

            xKeys.Add(new Keyframe(time, bakedQuaternion.x));
            yKeys.Add(new Keyframe(time, bakedQuaternion.y));
            zKeys.Add(new Keyframe(time, bakedQuaternion.z));
            wKeys.Add(new Keyframe(time, bakedQuaternion.w));
        }

        return new QuaternionCurves(
            new AnimationCurve(xKeys.ToArray()),
            new AnimationCurve(yKeys.ToArray()),
            new AnimationCurve(zKeys.ToArray()),
            new AnimationCurve(wKeys.ToArray()));
    }

    private static void AddCurveTimes(AnimationCurve curve, ISet<float> times)
    {
        foreach (var key in curve.keys)
        {
            times.Add(key.time);
        }
    }

    private static Quaternion EvaluateQuaternion(QuaternionCurves curves, float time)
    {
        return new Quaternion(
            curves.X.Evaluate(time),
            curves.Y.Evaluate(time),
            curves.Z.Evaluate(time),
            curves.W.Evaluate(time));
    }

    private static Quaternion Normalize(Quaternion value)
    {
        var magnitude = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
        if (magnitude < 0.0001f)
        {
            return Quaternion.identity;
        }

        var inverse = 1f / magnitude;
        return new Quaternion(value.x * inverse, value.y * inverse, value.z * inverse, value.w * inverse);
    }

    private static void SetQuaternionCurves(AnimationClip clip, string path, QuaternionCurves curves)
    {
        AnimationUtility.SetEditorCurve(clip, CreateRotationBinding(path, "m_LocalRotation.x"), curves.X);
        AnimationUtility.SetEditorCurve(clip, CreateRotationBinding(path, "m_LocalRotation.y"), curves.Y);
        AnimationUtility.SetEditorCurve(clip, CreateRotationBinding(path, "m_LocalRotation.z"), curves.Z);
        AnimationUtility.SetEditorCurve(clip, CreateRotationBinding(path, "m_LocalRotation.w"), curves.W);
    }

    private static void EnsureDistinctJumpStateSources(
        AnimatorController controller,
        AnimationClip jumpStart,
        AnimationClip jumpLoop,
        AnimationClip jumpEnd)
    {
        AssignStateMotion(controller, JumpStartStateName, jumpStart);
        AssignStateMotion(controller, JumpLoopStateName, jumpLoop);
        AssignStateMotion(controller, JumpEndStateName, jumpEnd);
    }

    private static void AssignStateMotion(AnimatorController controller, string stateName, Motion motion)
    {
        if (!TryFindState(controller.layers, stateName, out var state))
        {
            throw new InvalidOperationException($"Could not find animator state '{stateName}' in '{controller.name}'.");
        }

        state.motion = motion;
    }

    private static bool TryFindState(AnimatorControllerLayer[] layers, string stateName, out AnimatorState state)
    {
        foreach (var layer in layers)
        {
            if (TryFindState(layer.stateMachine, stateName, out state))
            {
                return true;
            }
        }

        state = null;
        return false;
    }

    private static bool TryFindState(AnimatorStateMachine stateMachine, string stateName, out AnimatorState state)
    {
        foreach (var childState in stateMachine.states)
        {
            if (string.Equals(childState.state.name, stateName, StringComparison.Ordinal))
            {
                state = childState.state;
                return true;
            }
        }

        foreach (var childStateMachine in stateMachine.stateMachines)
        {
            if (TryFindState(childStateMachine.stateMachine, stateName, out state))
            {
                return true;
            }
        }

        state = null;
        return false;
    }

    private static void ApplyFistJumpOverrides(
        AnimatorOverrideController overrideController,
        AnimationClip genericJumpStart,
        AnimationClip genericJumpLoop,
        AnimationClip genericJumpEnd,
        AnimationClip fistJumpStart,
        AnimationClip fistJumpLoop,
        AnimationClip fistJumpEnd)
    {
        overrideController[genericJumpStart.name] = fistJumpStart;
        overrideController[genericJumpLoop.name] = fistJumpLoop;
        overrideController[genericJumpEnd.name] = fistJumpEnd;
    }

    private readonly struct QuaternionCurves
    {
        public QuaternionCurves(AnimationCurve x, AnimationCurve y, AnimationCurve z, AnimationCurve w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public AnimationCurve X { get; }
        public AnimationCurve Y { get; }
        public AnimationCurve Z { get; }
        public AnimationCurve W { get; }
    }
}
