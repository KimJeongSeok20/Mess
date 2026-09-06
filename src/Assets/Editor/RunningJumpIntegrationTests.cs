using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Demo.Scripts.Runtime.Character;
using KINEMATION.FPSAnimationFramework.Runtime.Camera;
using KINEMATION.FPSAnimationFramework.Runtime.Layers.IkMotionLayer;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[Category("RunningJump")]
public sealed class RunningJumpIntegrationTests
{
    private const string SourceClipPath =
        "Assets/RetargetedAnimations/locomotion/RunningJump/CyberGenericBaked/Cyber_Generic_Running_Jump_Retargeted.anim";
    private const string InPlaceClipPath =
        "Assets/RetargetedAnimations/locomotion/RunningJump/CyberGenericBaked/Cyber_Generic_Running_Jump_InPlace.anim";
    private const string AirClipPath =
        "Assets/RetargetedAnimations/locomotion/RunningJump/CyberGenericBaked/Cyber_Generic_Running_Jump_Air.anim";
    private const string LandClipPath =
        "Assets/RetargetedAnimations/locomotion/RunningJump/CyberGenericBaked/Cyber_Generic_Running_Jump_Land.anim";
    private const string SourceClipSha256 = "2A89D4A269ECBBBC1A84AA3F299C7DFB488169903B98272288EF5EEC67DCD6F0";

    private static readonly string[] ControllerPaths =
    {
        "Assets/FPS/Cyber_Generic_FPSAnimator/Cyber_Generic.controller",
        "Assets/FPS/Cyber_Generic_FPSAnimator/FPSAnimator_Generic.controller"
    };

    private static readonly string[] ProductionOverrideControllerPaths =
    {
        "Assets/FPS/Cyber_Generic_FPSAnimator/Cyber_Unarmed_Generic.overrideController",
        "Assets/FPS/Weapon/AK v/FP_AK.overrideController",
        "Assets/FPS/Weapon/DGL50 v/FP_DGL50.overrideController",
        "Assets/FPS/Weapon/Drake-12 v/FP_Drake-12.overrideController",
        "Assets/FPS/Weapon/Fist v/FPSAnimator_Unarmed_Generic.overrideController",
        "Assets/FPS/Weapon/RPG v/FP_RPG.overrideController",
        "Assets/FPS/Weapon/Striker-V v/FP_Striker-V.overrideController"
    };

    private static readonly FieldInfo MovementSettingsField = typeof(FPSMovement).GetField(
        "movementSettings", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo InputDirectionField = typeof(FPSMovement).GetField(
        "_inputDirection", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo SprintHeldField = typeof(FPSMovement).GetField(
        "_sprintHeld", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo VelocityField = typeof(FPSMovement).GetField(
        "_velocity", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly PropertyInfo MovementStateProperty = typeof(FPSMovement).GetProperty(
        nameof(FPSMovement.MovementState), BindingFlags.Instance | BindingFlags.Public);
    private static readonly MethodInfo UpdateGroundedStateMethod = typeof(FPSMovement).GetMethod(
        "UpdateGroundedState", BindingFlags.Instance | BindingFlags.NonPublic);

    [Test]
    public void SourceClipRemainsUnmodifiedAndSplitClipsPreserveEveryBoundaryPose()
    {
        Assert.That(CalculateSha256(SourceClipPath), Is.EqualTo(SourceClipSha256));

        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(InPlaceClipPath);
        var air = AssetDatabase.LoadAssetAtPath<AnimationClip>(AirClipPath);
        var land = AssetDatabase.LoadAssetAtPath<AnimationClip>(LandClipPath);
        Assert.That(source, Is.Not.Null);
        Assert.That(air, Is.Not.Null);
        Assert.That(land, Is.Not.Null);
        Assert.That(air.frameRate, Is.EqualTo(source.frameRate).Within(0.001f));
        Assert.That(land.frameRate, Is.EqualTo(source.frameRate).Within(0.001f));
        Assert.That(air.length, Is.EqualTo(0.7f).Within(0.0001f));
        Assert.That(land.length, Is.EqualTo(0.2f).Within(0.0001f));
        Assert.That(air.isLooping, Is.False);
        Assert.That(land.isLooping, Is.False);

        var sourceBindings = AnimationUtility.GetCurveBindings(source)
            .Where(binding => binding.type != typeof(Transform) || !string.IsNullOrEmpty(binding.path))
            .ToArray();
        var airBindings = AnimationUtility.GetCurveBindings(air);
        var landBindings = AnimationUtility.GetCurveBindings(land);
        Assert.That(airBindings.Any(binding => binding.type == typeof(Transform)
                                               && string.IsNullOrEmpty(binding.path)), Is.False);
        Assert.That(landBindings.Any(binding => binding.type == typeof(Transform)
                                                && string.IsNullOrEmpty(binding.path)), Is.False);

        foreach (EditorCurveBinding sourceBinding in sourceBindings)
        {
            EditorCurveBinding airBinding = FindBinding(airBindings, sourceBinding);
            EditorCurveBinding landBinding = FindBinding(landBindings, sourceBinding);
            AnimationCurve sourceCurve = AnimationUtility.GetEditorCurve(source, sourceBinding);
            AnimationCurve airCurve = AnimationUtility.GetEditorCurve(air, airBinding);
            AnimationCurve landCurve = AnimationUtility.GetEditorCurve(land, landBinding);
            float expected = sourceCurve.Evaluate(0.7f);
            Assert.That(airCurve.Evaluate(0.7f), Is.EqualTo(expected).Within(0.00001f), BindingLabel(sourceBinding));
            Assert.That(landCurve.Evaluate(0f), Is.EqualTo(expected).Within(0.00001f), BindingLabel(sourceBinding));
        }

        AssertSprintWeightIsZero(air);
        AssertSprintWeightIsZero(land);
    }

    [TestCaseSource(nameof(ControllerPaths))]
    public void ControllersContainExclusiveRunningJumpBranch(string controllerPath)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        var airClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AirClipPath);
        var landClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(LandClipPath);
        Assert.That(controller, Is.Not.Null);
        Assert.That(controller.parameters.Single(parameter => parameter.name == "RunningJump").type,
            Is.EqualTo(AnimatorControllerParameterType.Bool));
        Assert.That(controller.parameters.Single(parameter => parameter.name == "RunningJumpAirSpeed").type,
            Is.EqualTo(AnimatorControllerParameterType.Float));
        Assert.That(controller.parameters.Single(parameter => parameter.name == "RunningJumpLand").type,
            Is.EqualTo(AnimatorControllerParameterType.Trigger));

        var inAir = controller.layers.Single(layer => layer.name == "InAir").stateMachine;
        var empty = FindState(inAir, "Empty");
        var jumpStart = FindState(inAir, "JumpStart");
        var jumpLoop = FindState(inAir, "JumpLoop");
        var jumpEnd = FindState(inAir, "JumpEnd");
        var runningJumpAir = FindState(inAir, "RunningJumpAir");
        var runningJumpLand = FindState(inAir, "RunningJumpLand");
        Assert.That(runningJumpAir.motion, Is.SameAs(airClip));
        Assert.That(runningJumpAir.speedParameterActive, Is.True);
        Assert.That(runningJumpAir.speedParameter, Is.EqualTo("RunningJumpAirSpeed"));
        Assert.That(runningJumpLand.motion, Is.SameAs(landClip));

        AssertTransition(empty, jumpStart, false, 0.1f,
            ("InAir", AnimatorConditionMode.If), ("RunningJump", AnimatorConditionMode.IfNot));
        AssertTransition(jumpEnd, jumpStart, false, 0.05f,
            ("InAir", AnimatorConditionMode.If), ("RunningJump", AnimatorConditionMode.IfNot));
        AssertTransition(empty, runningJumpAir, false, 0.06f,
            ("InAir", AnimatorConditionMode.If), ("RunningJump", AnimatorConditionMode.If));
        AssertTransition(jumpEnd, runningJumpAir, false, 0.06f,
            ("InAir", AnimatorConditionMode.If), ("RunningJump", AnimatorConditionMode.If));
        AssertTransition(runningJumpAir, runningJumpLand, false, 0.04f,
            ("RunningJumpLand", AnimatorConditionMode.If));
        var longFall = AssertTransition(runningJumpAir, jumpLoop, true, 0.05f,
            ("InAir", AnimatorConditionMode.If));
        Assert.That(longFall.exitTime, Is.EqualTo(1.02f).Within(0.001f));
        Assert.That(jumpLoop.transitions[0].destinationState, Is.SameAs(runningJumpLand));
        AssertTransition(jumpLoop, runningJumpLand, false, 0.04f,
            ("RunningJumpLand", AnimatorConditionMode.If));
        AssertTransition(runningJumpLand, runningJumpAir, false, 0.04f,
            ("InAir", AnimatorConditionMode.If), ("RunningJump", AnimatorConditionMode.If));
        AssertTransition(runningJumpLand, jumpStart, false, 0.04f,
            ("InAir", AnimatorConditionMode.If), ("RunningJump", AnimatorConditionMode.IfNot));
        var recovery = AssertTransition(runningJumpLand, empty, true, 0.06f);
        Assert.That(recovery.exitTime, Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void ProductionWeaponOverridesInheritBaseRunningJumpClip()
    {
        var baseController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPaths[0]);
        var air = AssetDatabase.LoadAssetAtPath<AnimationClip>(AirClipPath);
        var land = AssetDatabase.LoadAssetAtPath<AnimationClip>(LandClipPath);

        foreach (string path in ProductionOverrideControllerPaths)
        {
            var overrideController = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
            Assert.That(overrideController, Is.Not.Null, path);
            Assert.That(overrideController.runtimeAnimatorController, Is.SameAs(baseController), path);

            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrideController.GetOverrides(overrides);
            AssertClipIsInherited(overrides, air, path);
            AssertClipIsInherited(overrides, land, path);
        }
    }

    [Test]
    public void OnlyForwardSprintJumpActivatesRunningJumpAndLandingClearsIt()
    {
        GameObject player = null;
        FPSMovementSettings settings = null;
        try
        {
            Assert.That(MovementSettingsField, Is.Not.Null);
            Assert.That(InputDirectionField, Is.Not.Null);
            Assert.That(SprintHeldField, Is.Not.Null);
            Assert.That(VelocityField, Is.Not.Null);
            Assert.That(MovementStateProperty, Is.Not.Null);
            Assert.That(UpdateGroundedStateMethod, Is.Not.Null);

            player = new GameObject("RunningJumpMovementTest");
            var movement = player.AddComponent<FPSMovement>();
            settings = ScriptableObject.CreateInstance<FPSMovementSettings>();
            settings.jumpHeight = 4f;
            settings.gravity = 9f;
            MovementSettingsField.SetValue(movement, settings);
            InputDirectionField.SetValue(movement, Vector2.up);
            SetMovementState(movement, FPSMovementState.Sprinting);

            movement.OnJump();

            Assert.That(movement.MovementState, Is.EqualTo(FPSMovementState.InAir));
            Assert.That(movement.IsRunningJumpActive, Is.True);

            SprintHeldField.SetValue(movement, true);
            VelocityField.SetValue(movement, Vector3.down * 8f);
            movement._sprintActionCondition = () => true;
            UpdateGroundedStateMethod.Invoke(movement, new object[] { true, 0.016f });

            Assert.That(movement.IsRunningJumpActive, Is.False);
            Assert.That(movement.MovementState, Is.EqualTo(FPSMovementState.Sprinting));
            Assert.That(movement.LastLandingWasRunningJump, Is.True);
            Assert.That(movement.LastLandingImpactSpeed, Is.EqualTo(8f).Within(0.001f));
            Assert.That(movement.RunningJumpAirPhase01, Is.EqualTo(1f).Within(0.001f));
            Assert.That(movement.RunningJumpPlaybackSpeed, Is.EqualTo(0.7875f).Within(0.0001f));

            SetMovementState(movement, FPSMovementState.Walking);
            movement.OnJump();
            Assert.That(movement.IsRunningJumpActive, Is.False);
        }
        finally
        {
            if (player != null)
                UnityEngine.Object.DestroyImmediate(player);
            if (settings != null)
                UnityEngine.Object.DestroyImmediate(settings);
        }
    }

    [Test]
    public void UpwardImpulseNeverSelectsRunningJump()
    {
        var player = new GameObject("RunningJumpImpulseTest");
        try
        {
            var movement = player.AddComponent<FPSMovement>();
            movement.ApplyExternalImpulse(Vector3.up);
            Assert.That(movement.MovementState, Is.EqualTo(FPSMovementState.InAir));
            Assert.That(movement.IsRunningJumpActive, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void WalkOffAndDownwardExternalLandingNeverSelectRunningJump()
    {
        var player = new GameObject("RunningJumpWalkOffTest");
        try
        {
            var movement = player.AddComponent<FPSMovement>();
            SetMovementState(movement, FPSMovementState.Walking);
            UpdateGroundedStateMethod.Invoke(movement, new object[] { false, 0.09f });
            Assert.That(movement.MovementState, Is.EqualTo(FPSMovementState.InAir));
            Assert.That(movement.IsRunningJumpActive, Is.False);

            VelocityField.SetValue(movement, Vector3.down * 6f);
            UpdateGroundedStateMethod.Invoke(movement, new object[] { true, 0.016f });
            Assert.That(movement.LastLandingWasRunningJump, Is.False);
            Assert.That(movement.LastLandingImpactSpeed, Is.EqualTo(6f).Within(0.001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void AaaPresentationAssetsUseCyberRigAndRestrainedCameraChannels()
    {
        var takeoffIk = AssetDatabase.LoadAssetAtPath<IkMotionLayerSettings>(
            RunningJumpAaaAuthoring.TakeoffIkPath);
        var landingIk = AssetDatabase.LoadAssetAtPath<IkMotionLayerSettings>(
            RunningJumpAaaAuthoring.LandingIkPath);
        Assert.That(takeoffIk, Is.Not.Null);
        Assert.That(landingIk, Is.Not.Null);
        Assert.That(takeoffIk.boneToAnimate.index, Is.EqualTo(132));
        Assert.That(landingIk.boneToAnimate.index, Is.EqualTo(132));
        Assert.That(takeoffIk.playRate, Is.EqualTo(2f));
        Assert.That(landingIk.playRate, Is.EqualTo(2f));
        Assert.That(MaxMagnitude(takeoffIk.translationCurves), Is.LessThanOrEqualTo(0.02001f));
        Assert.That(MaxMagnitude(landingIk.translationCurves), Is.LessThanOrEqualTo(0.02501f));

        AssertCameraAsset(RunningJumpAaaAuthoring.TakeoffCameraPath, 0.30f, 0.012f, 0.35f);
        AssertCameraAsset(RunningJumpAaaAuthoring.LandingCameraPath, 0.24f, 0.025f, 0.65f);
        AssertCameraAsset(RunningJumpAaaAuthoring.HardLandingCameraPath, 0.32f, 0.035f, 1.1f);
    }

    [Test]
    public void SettingsPrefabAndAudioApiUseExplicitSingleMovementEventPath()
    {
        var settings = AssetDatabase.LoadAssetAtPath<FPSControllerSettings>(
            "Assets/FPS/Cyber_Generic_FPSAnimator/Cyber_FPSControllerSettings.asset");
        Assert.That(settings.runningJumpTakeoffMotion, Is.Not.Null);
        Assert.That(settings.runningJumpLandingMotion, Is.Not.Null);
        Assert.That(settings.runningJumpTakeoffCamera, Is.Not.Null);
        Assert.That(settings.runningJumpLandingCamera, Is.Not.Null);
        Assert.That(settings.hardRunningJumpLandingCamera, Is.Not.Null);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FPS/Cyber_Generic.prefab");
        PlayerSound sound = prefab.GetComponentInChildren<PlayerSound>(true);
        var serializedSound = new SerializedObject(sound);
        Assert.That(serializedSound.FindProperty("autoDetectJumpLand").boolValue, Is.False);
        Assert.That(typeof(PlayerSound).GetMethod(nameof(PlayerSound.PlayLand), new[] { typeof(float) }), Is.Not.Null);
    }

    [Test]
    public void NetworkContractCarriesBytePhaseAndOrdersAirAndLandingWrites()
    {
        FieldInfo phaseField = typeof(FPSController).GetField(
            "_netRunningJumpAirPhase", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(phaseField, Is.Not.Null);
        Assert.That(phaseField.FieldType.ToString(), Does.Contain("Byte"));

        string controllerSource = File.ReadAllText(
            "Assets/scriptable-animation-system-main/Assets/Demo/Scripts/Runtime/Character/FPSController.cs");
        int methodStart = controllerSource.IndexOf("private void SendLocomotionStateServerRpc", StringComparison.Ordinal);
        int methodEnd = controllerSource.IndexOf("[ServerRpc]", methodStart + 10, StringComparison.Ordinal);
        string method = controllerSource.Substring(methodStart, methodEnd - methodStart);
        Assert.That(method.IndexOf("_netRunningJumpAirPhase.value = runningJumpAirPhase", StringComparison.Ordinal),
            Is.LessThan(method.IndexOf("_netRunningJump.value = runningJump", StringComparison.Ordinal)));
        Assert.That(method.IndexOf("_netRunningJump.value = runningJump", StringComparison.Ordinal),
            Is.LessThan(method.IndexOf("_netGrounded.value = false", StringComparison.Ordinal)));
        Assert.That(method.IndexOf("_netGrounded.value = true", StringComparison.Ordinal),
            Is.LessThan(method.IndexOf("_netRunningJump.value = false", StringComparison.Ordinal)));
    }

    private static string CalculateSha256(string assetPath)
    {
        using var stream = File.OpenRead(Path.GetFullPath(assetPath));
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
    }

    private static EditorCurveBinding FindBinding(
        IEnumerable<EditorCurveBinding> bindings, EditorCurveBinding expected)
    {
        return bindings.Single(binding => binding.type == expected.type
                                          && binding.path == expected.path
                                          && binding.propertyName == expected.propertyName);
    }

    private static string BindingLabel(EditorCurveBinding binding)
    {
        return $"{binding.type.Name}:{binding.path}:{binding.propertyName}";
    }

    private static void AssertSprintWeightIsZero(AnimationClip clip)
    {
        EditorCurveBinding binding = AnimationUtility.GetCurveBindings(clip).Single(candidate =>
            candidate.type == typeof(Animator)
            && string.IsNullOrEmpty(candidate.path)
            && candidate.propertyName == "SprintWeight");
        AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
        Assert.That(curve.keys.All(key => Mathf.Abs(key.value) <= 0.00001f), Is.True, clip.name);
    }

    private static void AssertClipIsInherited(
        IEnumerable<KeyValuePair<AnimationClip, AnimationClip>> overrides, AnimationClip expected, string path)
    {
        KeyValuePair<AnimationClip, AnimationClip> item = overrides.FirstOrDefault(pair => pair.Key == expected);
        Assert.That(item.Key, Is.SameAs(expected), $"{path} does not expose {expected.name} from its base controller.");
        Assert.That(item.Value == null || item.Value == expected, Is.True, path);
    }

    private static float MaxMagnitude(KINEMATION.Shared.KAnimationCore.Runtime.Core.VectorCurve curves)
    {
        float end = curves.GetCurveLength();
        float maximum = 0f;
        for (int i = 0; i <= 1000; i++)
        {
            maximum = Mathf.Max(maximum, curves.GetValue(end * i / 1000f).magnitude);
        }
        return maximum;
    }

    private static void AssertCameraAsset(string path, float length, float maxVertical, float maxPitch)
    {
        var camera = AssetDatabase.LoadAssetAtPath<FPSCameraAnimation>(path);
        Assert.That(camera, Is.Not.Null, path);
        Assert.That(camera.translation.GetCurveLength(), Is.EqualTo(length).Within(0.0001f), path);
        Assert.That(camera.rotation.GetCurveLength(), Is.EqualTo(length).Within(0.0001f), path);
        Assert.That(MaxAbs(camera.translation.x), Is.LessThanOrEqualTo(0.000001f), path);
        Assert.That(MaxAbs(camera.translation.z), Is.LessThanOrEqualTo(0.000001f), path);
        Assert.That(MaxAbs(camera.rotation.y), Is.LessThanOrEqualTo(0.000001f), path);
        Assert.That(MaxAbs(camera.rotation.z), Is.LessThanOrEqualTo(0.000001f), path);
        Assert.That(MaxAbs(camera.translation.y), Is.LessThanOrEqualTo(maxVertical + 0.00001f), path);
        Assert.That(MaxAbs(camera.rotation.x), Is.LessThanOrEqualTo(maxPitch + 0.0001f), path);
    }

    private static float MaxAbs(AnimationCurve curve)
    {
        float end = curve.keys[^1].time;
        float maximum = 0f;
        for (int i = 0; i <= 1000; i++)
        {
            maximum = Mathf.Max(maximum, Mathf.Abs(curve.Evaluate(end * i / 1000f)));
        }
        return maximum;
    }

    private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
    {
        return stateMachine.states.Single(child => child.state.name == stateName).state;
    }

    private static AnimatorStateTransition AssertTransition(
        AnimatorState source,
        AnimatorState destination,
        bool hasExitTime,
        float duration,
        params (string parameter, AnimatorConditionMode mode)[] expectedConditions)
    {
        var transition = source.transitions.Single(candidate => candidate.destinationState == destination);
        Assert.That(transition.hasExitTime, Is.EqualTo(hasExitTime));
        Assert.That(transition.hasFixedDuration, Is.True);
        Assert.That(transition.duration, Is.EqualTo(duration).Within(0.001f));
        Assert.That(transition.conditions.Length, Is.EqualTo(expectedConditions.Length));

        foreach (var expected in expectedConditions)
        {
            Assert.That(transition.conditions.Any(condition => condition.parameter == expected.parameter
                                                               && condition.mode == expected.mode), Is.True);
        }

        return transition;
    }

    private static void SetMovementState(FPSMovement movement, FPSMovementState state)
    {
        MovementStateProperty.SetValue(movement, state);
    }
}
