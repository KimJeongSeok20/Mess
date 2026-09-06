using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates sprint clips whose lower body still runs while the upper body stays in the selected weapon's idle pose.
/// AK, Drake-12, RPG, and Striker-V keep the original Kinemation weapon-tuck motion,
/// while DGL50 receives its own DGL-idle version.
/// </summary>
public static class WeaponSprintStabilizationAuthoringTool
{
    private const string OriginalSprintPath = "Assets/RetargetedAnimations/locomotion/Standing/Cyber_Retarget_C_Rifle_Sprint_Fwd.anim";
    private const string SprintWithoutEventsPath = "Assets/RetargetedAnimations/locomotion/Standing/Cyber_Retarget_C_Rifle_Sprint_Fwd_noevent.anim";
    private const string RifleIdlePath = "Assets/RetargetedAnimations/locomotion/Standing/Cyber_Retarget_C_Rifle_Idle.anim";
    private const string StabilizedSprintPath = "Assets/RetargetedAnimations/locomotion/Standing/Cyber_Retarget_C_Rifle_Sprint_Fwd_Stabilized.anim";
    private const string DglIdlePath = "Assets/RetargetedAnimations/DGL/Cyber_Retarget_A_FP_DGL50_idle.anim";
    private const string DglStabilizedSprintPath = "Assets/RetargetedAnimations/DGL/Cyber_Retarget_C_DGL50_Sprint_Fwd_Stabilized.anim";
    private const string UnarmedSprintPath = "Assets/RetargetedAnimations/locomotion/Cyber_Retarget_C_Unarmed_Sprint_Fwd.anim";
    private const string AkOverridePath = "Assets/FPS/Weapon/AK v/FP_AK.overrideController";
    private const string ShotgunOverridePath = "Assets/FPS/Weapon/Drake-12 v/FP_Drake-12.overrideController";
    private const string RpgOverridePath = "Assets/FPS/Weapon/RPG v/FP_RPG.overrideController";
    private const string StrikerOverridePath = "Assets/FPS/Weapon/Striker-V v/FP_Striker-V.overrideController";

    private const string UpperBodyRootPath = "root/pelvis/spine_01/spine_02/spine_03/spine_04";

    private static readonly string[] TargetOverridePaths = Array.Empty<string>();

    private const string DglOverridePath = "Assets/FPS/Weapon/DGL50 v/FP_DGL50.overrideController";

    private static readonly string[] UntouchedOverridePaths =
    {
        "Assets/FPS/Weapon/Fist v/FPSAnimator_Unarmed_Generic.overrideController"
    };

    [MenuItem("Tools/Weapons/Generate Stabilized Cyber Sprint")]
    public static void GenerateAndApplyMenu()
    {
        Debug.Log(GenerateAndApply());
    }

    public static string GenerateAndApply()
    {
        AnimationClip originalSprint = LoadClip(OriginalSprintPath);
        AnimationClip sprintWithoutEvents = LoadClip(SprintWithoutEventsPath);
        AnimationClip rifleIdle = LoadClip(RifleIdlePath);
        AnimationClip dglIdle = LoadClip(DglIdlePath);

        AnimationClip stabilizedSprint = CreateOrUpdateStabilizedSprint(sprintWithoutEvents, rifleIdle);
        AnimationClip dglStabilizedSprint = CreateOrUpdateStabilizedSprint(
            sprintWithoutEvents, dglIdle, DglStabilizedSprintPath);
        foreach (string overridePath in TargetOverridePaths)
        {
            AnimatorOverrideController controller = LoadOverrideController(overridePath);
            AnimationClip current = controller[originalSprint.name];
            if (current != sprintWithoutEvents && current != stabilizedSprint)
            {
                throw new InvalidOperationException(
                    $"'{overridePath}' has unexpected sprint override '{current?.name ?? "null"}'. Expected '{sprintWithoutEvents.name}' or '{stabilizedSprint.name}'.");
            }

            controller[originalSprint.name] = stabilizedSprint;
            EditorUtility.SetDirty(controller);
        }

        ApplyKinemationSprintOverride(AkOverridePath, originalSprint, sprintWithoutEvents, stabilizedSprint);
        ApplyKinemationSprintOverride(ShotgunOverridePath, originalSprint, sprintWithoutEvents, stabilizedSprint);
        ApplyKinemationSprintOverride(RpgOverridePath, originalSprint, sprintWithoutEvents, stabilizedSprint);
        ApplyKinemationSprintOverride(StrikerOverridePath, originalSprint, sprintWithoutEvents, stabilizedSprint);
        ApplyDglSprintOverride(originalSprint, dglStabilizedSprint);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return Verify();
    }

    public static string Verify()
    {
        AnimationClip originalSprint = LoadClip(OriginalSprintPath);
        AnimationClip sprintWithoutEvents = LoadClip(SprintWithoutEventsPath);
        AnimationClip stabilizedSprint = LoadClip(StabilizedSprintPath);
        AnimationClip dglStabilizedSprint = LoadClip(DglStabilizedSprintPath);

        AnimatorOverrideController akController = LoadOverrideController(AkOverridePath);
        bool akUsesKinemationSprint = akController[originalSprint.name] == originalSprint;
        if (!akUsesKinemationSprint)
        {
            throw new InvalidOperationException(
                $"'{AkOverridePath}' does not use '{originalSprint.name}' for '{originalSprint.name}'.");
        }

        AnimatorOverrideController shotgunController = LoadOverrideController(ShotgunOverridePath);
        bool shotgunUsesKinemationSprint = shotgunController[originalSprint.name] == originalSprint;
        if (!shotgunUsesKinemationSprint)
        {
            throw new InvalidOperationException(
                $"'{ShotgunOverridePath}' does not use '{originalSprint.name}' for '{originalSprint.name}'.");
        }

        AnimatorOverrideController rpgController = LoadOverrideController(RpgOverridePath);
        bool rpgUsesKinemationSprint = rpgController[originalSprint.name] == originalSprint;
        if (!rpgUsesKinemationSprint)
        {
            throw new InvalidOperationException(
                $"'{RpgOverridePath}' does not use '{originalSprint.name}' for '{originalSprint.name}'.");
        }

        AnimatorOverrideController strikerController = LoadOverrideController(StrikerOverridePath);
        bool strikerUsesKinemationSprint = strikerController[originalSprint.name] == originalSprint;
        if (!strikerUsesKinemationSprint)
        {
            throw new InvalidOperationException(
                $"'{StrikerOverridePath}' does not use '{originalSprint.name}' for '{originalSprint.name}'.");
        }

        var targetResults = new List<string>();
        foreach (string overridePath in TargetOverridePaths)
        {
            AnimatorOverrideController controller = LoadOverrideController(overridePath);
            bool isAssigned = controller[originalSprint.name] == stabilizedSprint;
            targetResults.Add($"{Path.GetFileNameWithoutExtension(overridePath)}={(isAssigned ? "STABILIZED" : controller[originalSprint.name]?.name ?? "null")}");
            if (!isAssigned)
            {
                throw new InvalidOperationException($"'{overridePath}' does not use '{stabilizedSprint.name}' for '{originalSprint.name}'.");
            }
        }

        AnimatorOverrideController dglController = LoadOverrideController(DglOverridePath);
        bool dglIsAssigned = dglController[originalSprint.name] == dglStabilizedSprint;
        if (!dglIsAssigned)
        {
            throw new InvalidOperationException(
                $"'{DglOverridePath}' does not use '{dglStabilizedSprint.name}' for '{originalSprint.name}'.");
        }

        var untouchedResults = new List<string>();
        foreach (string overridePath in UntouchedOverridePaths)
        {
            AnimatorOverrideController controller = LoadOverrideController(overridePath);
            AnimationClip assigned = controller[originalSprint.name];
            bool remainsUntouched = assigned != stabilizedSprint;
            untouchedResults.Add($"{Path.GetFileNameWithoutExtension(overridePath)}={(remainsUntouched ? assigned?.name ?? "null" : "ERR_STABILIZED")}");
            if (!remainsUntouched)
            {
                throw new InvalidOperationException($"'{overridePath}' must not use '{stabilizedSprint.name}'.");
            }
        }

        return $"[WeaponSprintStabilization] ak={Path.GetFileNameWithoutExtension(AkOverridePath)}={originalSprint.name} shotgun={Path.GetFileNameWithoutExtension(ShotgunOverridePath)}={originalSprint.name} rpg={Path.GetFileNameWithoutExtension(RpgOverridePath)}={originalSprint.name} striker={Path.GetFileNameWithoutExtension(StrikerOverridePath)}={originalSprint.name} rifleClip={stabilizedSprint.name} target=[{string.Join(", ", targetResults)}] dgl={Path.GetFileNameWithoutExtension(DglOverridePath)}={dglStabilizedSprint.name} untouched=[{string.Join(", ", untouchedResults)}] source={sprintWithoutEvents.name}";
    }

    private static AnimationClip CreateOrUpdateStabilizedSprint(AnimationClip sprintClip, AnimationClip idleClip)
    {
        return CreateOrUpdateStabilizedSprint(sprintClip, idleClip, StabilizedSprintPath);
    }

    private static AnimationClip CreateOrUpdateStabilizedSprint(AnimationClip sprintClip, AnimationClip idleClip, string targetPath)
    {
        EnsureParentDirectoryExists(targetPath);
        AnimationClip target = AssetDatabase.LoadAssetAtPath<AnimationClip>(targetPath);
        if (target == null)
        {
            target = new AnimationClip();
            AssetDatabase.CreateAsset(target, targetPath);
        }

        EditorUtility.CopySerialized(sprintClip, target);
        target.name = Path.GetFileNameWithoutExtension(targetPath);

        int stabilizedPathCount = 0;
        foreach (string path in CollectUpperBodyRotationPaths(sprintClip, idleClip))
        {
            if (!TryGetQuaternionCurves(idleClip, path, out QuaternionCurves idleCurves))
            {
                continue;
            }

            SetConstantQuaternionCurves(target, path, EvaluateQuaternion(idleCurves, 0f), sprintClip.length);
            stabilizedPathCount++;
        }

        if (stabilizedPathCount == 0)
        {
            throw new InvalidOperationException("No shared upper-body rotation paths were found for the Cyber rifle sprint and idle clips.");
        }

        EditorUtility.SetDirty(target);
        return target;
    }

    private static void ApplyDglSprintOverride(AnimationClip originalSprint, AnimationClip dglStabilizedSprint)
    {
        AnimatorOverrideController controller = LoadOverrideController(DglOverridePath);
        AnimationClip unarmedSprint = LoadClip(UnarmedSprintPath);
        AnimationClip current = controller[originalSprint.name];
        if (current != originalSprint && current != unarmedSprint && current != dglStabilizedSprint)
        {
            throw new InvalidOperationException(
                $"'{DglOverridePath}' has unexpected sprint override '{current?.name ?? "null"}'. Expected '{originalSprint.name}', '{unarmedSprint.name}', or '{dglStabilizedSprint.name}'.");
        }

        controller[originalSprint.name] = dglStabilizedSprint;
        EditorUtility.SetDirty(controller);
    }

    private static void ApplyKinemationSprintOverride(
        string overridePath,
        AnimationClip originalSprint,
        AnimationClip sprintWithoutEvents,
        AnimationClip stabilizedSprint)
    {
        AnimatorOverrideController controller = LoadOverrideController(overridePath);
        AnimationClip current = controller[originalSprint.name];
        if (current != originalSprint && current != sprintWithoutEvents && current != stabilizedSprint)
        {
            throw new InvalidOperationException(
                $"'{overridePath}' has unexpected sprint override '{current?.name ?? "null"}'. Expected '{originalSprint.name}', '{sprintWithoutEvents.name}', or '{stabilizedSprint.name}'.");
        }

        // The original clip carries the FullBodyWeight/SprintPoseWeight curves that
        // disable the Kinemation pose sampler while sprinting. The generated
        // no-event derivative contains no events either, but lost those curves.
        controller[originalSprint.name] = originalSprint;
        EditorUtility.SetDirty(controller);
    }

    private static AnimationClip LoadClip(string assetPath)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
        if (clip == null)
        {
            throw new InvalidOperationException($"Missing animation clip at '{assetPath}'.");
        }

        return clip;
    }

    private static AnimatorOverrideController LoadOverrideController(string assetPath)
    {
        AnimatorOverrideController controller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(assetPath);
        if (controller == null)
        {
            throw new InvalidOperationException($"Missing Animator Override Controller at '{assetPath}'.");
        }

        return controller;
    }

    private static void EnsureParentDirectoryExists(string assetPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(assetPath));
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static List<string> CollectUpperBodyRotationPaths(AnimationClip sprintClip, AnimationClip idleClip)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        AddUpperBodyRotationPaths(sprintClip, paths);
        AddUpperBodyRotationPaths(idleClip, paths);
        return new List<string>(paths);
    }

    private static void AddUpperBodyRotationPaths(AnimationClip clip, ISet<string> paths)
    {
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (!binding.propertyName.StartsWith("m_LocalRotation", StringComparison.Ordinal) || !IsUpperBodyPath(binding.path))
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
        AnimationCurve x = AnimationUtility.GetEditorCurve(clip, RotationBinding(path, "m_LocalRotation.x"));
        AnimationCurve y = AnimationUtility.GetEditorCurve(clip, RotationBinding(path, "m_LocalRotation.y"));
        AnimationCurve z = AnimationUtility.GetEditorCurve(clip, RotationBinding(path, "m_LocalRotation.z"));
        AnimationCurve w = AnimationUtility.GetEditorCurve(clip, RotationBinding(path, "m_LocalRotation.w"));
        if (x == null || y == null || z == null || w == null)
        {
            curves = default;
            return false;
        }

        curves = new QuaternionCurves(x, y, z, w);
        return true;
    }

    private static EditorCurveBinding RotationBinding(string path, string propertyName)
    {
        return EditorCurveBinding.FloatCurve(path, typeof(Transform), propertyName);
    }

    private static Quaternion EvaluateQuaternion(QuaternionCurves curves, float time)
    {
        Quaternion rotation = new Quaternion(
            curves.X.Evaluate(time),
            curves.Y.Evaluate(time),
            curves.Z.Evaluate(time),
            curves.W.Evaluate(time));
        float magnitudeSquared = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
        return magnitudeSquared > 0.0001f ? rotation.normalized : Quaternion.identity;
    }

    private static void SetConstantQuaternionCurves(AnimationClip clip, string path, Quaternion rotation, float duration)
    {
        float end = Mathf.Max(duration, 0.01f);
        AnimationCurve x = ConstantCurve(rotation.x, end);
        AnimationCurve y = ConstantCurve(rotation.y, end);
        AnimationCurve z = ConstantCurve(rotation.z, end);
        AnimationCurve w = ConstantCurve(rotation.w, end);

        AnimationUtility.SetEditorCurve(clip, RotationBinding(path, "m_LocalRotation.x"), x);
        AnimationUtility.SetEditorCurve(clip, RotationBinding(path, "m_LocalRotation.y"), y);
        AnimationUtility.SetEditorCurve(clip, RotationBinding(path, "m_LocalRotation.z"), z);
        AnimationUtility.SetEditorCurve(clip, RotationBinding(path, "m_LocalRotation.w"), w);
    }

    private static AnimationCurve ConstantCurve(float value, float duration)
    {
        return new AnimationCurve(new Keyframe(0f, value), new Keyframe(duration, value));
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
