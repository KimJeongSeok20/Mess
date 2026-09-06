using System;
using System.Collections.Generic;
using System.IO;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEditor;
using UnityEngine;

public static class RoomPowerPhoneAuthoring
{
    public const string SourceClipPath =
        "Assets/RetargetedAnimations/locomotion/TakingItem/CyberGenericBaked/Cyber_Generic_Taking_Item_Retargeted.anim";
    public const string LookClipPath =
        "Assets/RetargetedAnimations/locomotion/TakingItem/CyberGenericBaked/Cyber_Generic_Phone_Look_127_140.anim";
    public const string HoldClipPath =
        "Assets/RetargetedAnimations/locomotion/TakingItem/CyberGenericBaked/Cyber_Generic_Phone_Look_Hold.anim";
    public const string LookAssetPath =
        "Assets/FPS/Cyber_Generic_FPSAnimator/A_Phone_Look.asset";
    public const string HoldAssetPath =
        "Assets/FPS/Cyber_Generic_FPSAnimator/A_Phone_Look_Hold.asset";
    public const string RigPath = "Assets/FPS/Cyber_Generic_FPSAnimator/Rig_Cyber_Generic.asset";
    public const string MaskPath = "Assets/FPS/Cyber_Generic_FPSAnimator/Cyber_UpperMask.mask";
    public const string PlayerPrefabPath = "Assets/FPS/Cyber_Generic.prefab";

    public const int LookStartFrame = 127;
    public const int LookEndFrame = 140;

    [MenuItem("Tools/StillWorking/Room Power/Build Phone Look")]
    public static void BuildMenu()
    {
        Debug.Log(Build());
    }

    public static string Build()
    {
        AnimationClip source = LoadRequired<AnimationClip>(SourceClipPath);
        float frameRate = source.frameRate > 0.01f ? source.frameRate : 30f;
        float startTime = LookStartFrame / frameRate;
        float endTime = Mathf.Min(source.length, LookEndFrame / frameRate);
        if (endTime <= startTime)
        {
            throw new InvalidOperationException(
                $"Phone look window is empty: start={startTime:F3}s end={endTime:F3}s length={source.length:F3}s.");
        }

        AnimationClip lookClip = BuildClipSlice(source, LookClipPath, startTime, endTime);
        float holdStart = Mathf.Max(startTime, endTime - (1f / frameRate));
        AnimationClip holdClip = BuildClipSlice(source, HoldClipPath, holdStart, endTime);

        AnimationClipSettings holdSettings = AnimationUtility.GetAnimationClipSettings(holdClip);
        holdSettings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(holdClip, holdSettings);
        EditorUtility.SetDirty(holdClip);

        KRig rig = LoadRequired<KRig>(RigPath);
        AvatarMask mask = LoadRequired<AvatarMask>(MaskPath);
        FPSAnimationAsset lookAsset = CreateOrUpdateAnimationAsset(
            LookAssetPath, lookClip, rig, mask, new BlendTime(0.12f, 0f));
        StretchClipToDuration(holdClip, 1f);
        FPSAnimationAsset holdAsset = CreateOrUpdateAnimationAsset(
            HoldAssetPath, holdClip, rig, mask, new BlendTime(0.35f, 0.28f));

        AssignToPlayerPrefab(lookAsset, holdAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return
            $"Phone look built: frames {LookStartFrame}-{LookEndFrame} " +
            $"({startTime:F3}s-{endTime:F3}s, fps={frameRate:F1}) " +
            $"look='{LookClipPath}' hold='{HoldClipPath}'.";
    }

    [MenuItem("Tools/StillWorking/Room Power/Rebuild Phone Hold Pose")]
    public static void RebuildHoldMenu()
    {
        Debug.Log(RebuildHoldPose());
    }

    public static string RebuildHoldPose()
    {
        AnimationClip holdClip = LoadRequired<AnimationClip>(HoldClipPath);
        StretchClipToDuration(holdClip, 1f);
        FPSAnimationAsset holdAsset = LoadRequired<FPSAnimationAsset>(HoldAssetPath);
        holdAsset.blendTime = new BlendTime(0.35f, 0.28f);
        EditorUtility.SetDirty(holdClip);
        EditorUtility.SetDirty(holdAsset);
        AssetDatabase.SaveAssets();
        return $"Phone hold pose stretched to {holdClip.length:F3}s, blend in 0.35s / out 0.28s.";
    }

    private static void StretchClipToDuration(AnimationClip clip, float duration)
    {
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length == 0)
                continue;

            Keyframe last = curve.keys[curve.length - 1];
            if (last.time + 0.0001f >= duration)
                continue;

            var keys = new List<Keyframe>(curve.keys)
            {
                new Keyframe(duration, last.value, 0f, 0f)
            };
            curve.keys = keys.ToArray();
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        settings.loopBlend = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        clip.wrapMode = WrapMode.ClampForever;
        EditorUtility.SetDirty(clip);
    }

    private static void AssignToPlayerPrefab(FPSAnimationAsset lookAsset, FPSAnimationAsset holdAsset)
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            var phone = prefabRoot.GetComponent<DungeonRoomPowerPhone>();
            if (phone == null)
                phone = prefabRoot.AddComponent<DungeonRoomPowerPhone>();

            var serialized = new SerializedObject(phone);
            serialized.FindProperty("lookAnimation").objectReferenceValue = lookAsset;
            serialized.FindProperty("holdPose").objectReferenceValue = holdAsset;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static FPSAnimationAsset CreateOrUpdateAnimationAsset(
        string path,
        AnimationClip clip,
        KRig rig,
        AvatarMask mask,
        BlendTime blendTime)
    {
        FPSAnimationAsset asset = AssetDatabase.LoadAssetAtPath<FPSAnimationAsset>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<FPSAnimationAsset>();
            AssetDatabase.CreateAsset(asset, path);
        }

        asset.rigAsset = rig;
        asset.clip = clip;
        asset.mask = mask;
        asset.overrideMask = null;
        asset.isAdditive = false;
        asset.blendTime = blendTime;
        asset.curves = new List<AnimCurve>();
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static T LoadRequired<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
            throw new InvalidOperationException($"Required asset is missing: {path}");
        return asset;
    }

    private static AnimationClip BuildClipSlice(AnimationClip source, string path, float startTime, float endTime)
    {
        EnsureParentDirectoryExists(path);

        AnimationClip target = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (target == null)
        {
            target = new AnimationClip();
            AssetDatabase.CreateAsset(target, path);
        }

        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(target))
            AnimationUtility.SetEditorCurve(target, binding, null);

        foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(target))
            AnimationUtility.SetObjectReferenceCurve(target, binding, null);

        target.name = Path.GetFileNameWithoutExtension(path);
        target.frameRate = source.frameRate;
        target.wrapMode = WrapMode.ClampForever;
        target.legacy = source.legacy;
        target.localBounds = source.localBounds;

        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
        {
            if (binding.type == typeof(Transform) && string.IsNullOrEmpty(binding.path))
                continue;

            AnimationCurve sourceCurve = AnimationUtility.GetEditorCurve(source, binding);
            AnimationUtility.SetEditorCurve(target, binding, SliceCurve(sourceCurve, startTime, endTime));
        }

        foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
        {
            ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(source, binding);
            AnimationUtility.SetObjectReferenceCurve(target, binding, SliceObjectCurve(keys, startTime, endTime));
        }

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
                continue;

            Keyframe shifted = sourceKey;
            shifted.time -= startTime;
            keys.Add(shifted);
        }

        keys.Add(CreateBoundaryKey(source, endTime, length));
        return new AnimationCurve(keys.ToArray())
        {
            preWrapMode = source.preWrapMode,
            postWrapMode = source.postWrapMode
        };
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
            return Array.Empty<ObjectReferenceKeyframe>();

        UnityEngine.Object ValueAt(float time)
        {
            UnityEngine.Object value = source[0].value;
            foreach (ObjectReferenceKeyframe key in source)
            {
                if (key.time > time + 0.000001f)
                    break;
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
            if (key.time <= startTime + 0.000001f || key.time >= endTime - 0.000001f)
                continue;
            result.Add(new ObjectReferenceKeyframe { time = key.time - startTime, value = key.value });
        }

        result.Add(new ObjectReferenceKeyframe { time = endTime - startTime, value = ValueAt(endTime) });
        return result.ToArray();
    }

    private static void EnsureParentDirectoryExists(string assetPath)
    {
        string directory = Path.GetDirectoryName(assetPath);
        if (string.IsNullOrEmpty(directory) || Directory.Exists(directory))
            return;

        Directory.CreateDirectory(directory);
    }
}
