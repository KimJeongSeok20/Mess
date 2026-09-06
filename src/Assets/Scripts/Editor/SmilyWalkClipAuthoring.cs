using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class SmilyWalkClipAuthoring
{
    private const string SourceClipPath = "Assets/Monster/smily/smily-horror-monster_cc_attribution/KA_Crawling_Baby_Walk_Fwd_Start_7 1.anim";
    private const string OutputClipPath = "Assets/Monster/smily/smily-horror-monster_cc_attribution/KA_Crawling_Baby_Walk_Fwd_Start_7_1_TrimmedLoop.anim";
    private const string ControllerPath = "Assets/Monster/smily/smily-horror-monster_cc_attribution/smily_animator.controller";

    private const int EarlyCutStartFrame = 2;
    private const int EarlyCutEndFrameInclusive = 4;
    private const int EarlyJoinBlendFrames = 1;
    private const int CutStartFrame = 31;
    private const int CutEndFrameInclusive = 37;
    private const int JoinBlendFrames = 4;
    private const float SeamWindowNormalized = 0.15f;

    public static string CreateTrimmedStart7WalkCopy()
    {
        var sourceClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(SourceClipPath);
        if (sourceClip == null)
            throw new InvalidOperationException($"Missing source clip at '{SourceClipPath}'.");

        EnsureParentDirectoryExists(OutputClipPath);

        var outputClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputClipPath);
        if (outputClip == null)
        {
            outputClip = new AnimationClip
            {
                name = Path.GetFileNameWithoutExtension(OutputClipPath)
            };

            AssetDatabase.CreateAsset(outputClip, OutputClipPath);
            outputClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputClipPath);
        }

        EditorUtility.CopySerialized(sourceClip, outputClip);
        outputClip.name = Path.GetFileNameWithoutExtension(OutputClipPath);

        ClearAllCurves(outputClip);

        var frameRate = sourceClip.frameRate > 0f ? sourceClip.frameRate : 30f;
        var laterCutStartTime = CutStartFrame / frameRate;
        var laterCutEndTime = (CutEndFrameInclusive + 1) / frameRate;
        var laterJoinBlendDuration = JoinBlendFrames / frameRate;

        var earlyCutStartTime = EarlyCutStartFrame / frameRate;
        var earlyCutEndTime = (EarlyCutEndFrameInclusive + 1) / frameRate;
        var earlyJoinBlendDuration = EarlyJoinBlendFrames / frameRate;

        foreach (var binding in AnimationUtility.GetCurveBindings(sourceClip))
        {
            var sourceCurve = AnimationUtility.GetEditorCurve(sourceClip, binding);
            if (sourceCurve == null)
                continue;

            var trimmedLaterCurve = RemoveSegment(sourceCurve, frameRate, laterCutStartTime, laterCutEndTime, laterJoinBlendDuration, sourceClip.length);
            var trimmedDuration = trimmedLaterCurve.keys[trimmedLaterCurve.length - 1].time;
            var trimmedEarlyCurve = RemoveSegment(trimmedLaterCurve, frameRate, earlyCutStartTime, earlyCutEndTime, earlyJoinBlendDuration, trimmedDuration);
            var finalDuration = trimmedEarlyCurve.keys[trimmedEarlyCurve.length - 1].time;
            var seamClosedCurve = CloseLoopSeam(trimmedEarlyCurve, finalDuration);
            AnimationUtility.SetEditorCurve(outputClip, binding, seamClosedCurve);
        }

        ApplyLoopSettings(outputClip);
        AssignWalkMotion(outputClip);

        EditorUtility.SetDirty(outputClip);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return $"Created '{OutputClipPath}' from '{SourceClipPath}' with frames {EarlyCutStartFrame}-{EarlyCutEndFrameInclusive} and {CutStartFrame}-{CutEndFrameInclusive} removed, then assigned it as SMILY walk.";
    }

    private static void EnsureParentDirectoryExists(string assetPath)
    {
        var fullPath = Path.GetFullPath(assetPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }

    private static void ClearAllCurves(AnimationClip clip)
    {
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            AnimationUtility.SetEditorCurve(clip, binding, null);
    }

    private static AnimationCurve RemoveSegment(
        AnimationCurve sourceCurve,
        float frameRate,
        float cutStartTime,
        float cutEndTime,
        float joinBlendDuration,
        float sourceDuration)
    {
        var cutDuration = cutEndTime - cutStartTime;
        var outputDuration = sourceDuration - cutDuration;
        var sampleCount = Mathf.RoundToInt(outputDuration * frameRate);
        var keys = new List<Keyframe>(sampleCount + 1);
        var blendStartTime = Mathf.Max(0f, cutStartTime - joinBlendDuration);

        for (var frame = 0; frame <= sampleCount; frame++)
        {
            var time = Mathf.Min(frame / frameRate, outputDuration);
            float value;

            if (time < blendStartTime)
            {
                value = sourceCurve.Evaluate(time);
            }
            else if (time < cutStartTime)
            {
                var blend = Mathf.InverseLerp(blendStartTime, cutStartTime, time);
                var preValue = sourceCurve.Evaluate(time);
                var postValue = sourceCurve.Evaluate(Mathf.Min(time + cutDuration, sourceDuration));
                value = Mathf.Lerp(preValue, postValue, blend);
            }
            else
            {
                value = sourceCurve.Evaluate(Mathf.Min(time + cutDuration, sourceDuration));
            }

            keys.Add(new Keyframe(time, value));
        }

        return new AnimationCurve(keys.ToArray())
        {
            preWrapMode = sourceCurve.preWrapMode,
            postWrapMode = sourceCurve.postWrapMode
        };
    }

    private static AnimationCurve CloseLoopSeam(AnimationCurve curve, float duration)
    {
        var seamStart = duration * (1f - SeamWindowNormalized);
        var seamDuration = Mathf.Max(0.0001f, duration - seamStart);
        var startValue = curve.Evaluate(0f);
        var keys = curve.keys;

        for (var i = 0; i < keys.Length; i++)
        {
            if (keys[i].time < seamStart)
                continue;

            var blend = Mathf.SmoothStep(0f, 1f, (keys[i].time - seamStart) / seamDuration);
            keys[i].value = Mathf.Lerp(keys[i].value, startValue, blend);
        }

        if (keys.Length > 0)
            keys[keys.Length - 1].value = startValue;

        var closedCurve = new AnimationCurve(keys)
        {
            preWrapMode = curve.preWrapMode,
            postWrapMode = curve.postWrapMode
        };

        return closedCurve;
    }

    private static void ApplyLoopSettings(AnimationClip clip)
    {
        var serializedObject = new SerializedObject(clip);
        var settings = serializedObject.FindProperty("m_AnimationClipSettings");
        if (settings == null)
            throw new InvalidOperationException($"Could not find animation clip settings for '{clip.name}'.");

        settings.FindPropertyRelative("m_LoopTime").boolValue = true;
        settings.FindPropertyRelative("m_LoopBlend").boolValue = true;

        var orientation = settings.FindPropertyRelative("m_LoopBlendOrientation");
        if (orientation != null)
            orientation.boolValue = true;

        var positionY = settings.FindPropertyRelative("m_LoopBlendPositionY");
        if (positionY != null)
            positionY.boolValue = true;

        var positionXZ = settings.FindPropertyRelative("m_LoopBlendPositionXZ");
        if (positionXZ != null)
            positionXZ.boolValue = true;

        serializedObject.ApplyModifiedProperties();
    }

    private static void AssignWalkMotion(Motion motion)
    {
        var blendTree = AssetDatabase.LoadAllAssetsAtPath(ControllerPath).OfType<BlendTree>().FirstOrDefault();
        if (blendTree == null)
            throw new InvalidOperationException($"Could not find a BlendTree asset in '{ControllerPath}'.");

        var children = blendTree.children;
        if (children.Length < 2)
            throw new InvalidOperationException("SMILY blend tree does not have the expected two motions.");

        children[1].motion = motion;
        blendTree.children = children;
        EditorUtility.SetDirty(blendTree);
    }
}
