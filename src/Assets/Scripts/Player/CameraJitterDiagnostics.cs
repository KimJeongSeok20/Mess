using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.FPSAnimationFramework.Runtime.Recoil;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using PurrNet;
using System;
using System.Reflection;
using UnityEngine;

namespace Demo.Scripts.Runtime.Character
{
    [DisallowMultipleComponent]
    public sealed class CameraJitterDiagnostics : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private FPSController fpsController;
        [SerializeField] private FPSMovement movement;
        [SerializeField] private FPSLookController lookController;
        [SerializeField] private RecoilAnimation recoilAnimation;
        [SerializeField] private RecoilPattern recoilPattern;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Transform viewModelRoot;

        [Header("Sampling")]
        [SerializeField, Min(0.25f)] private float reportInterval = 1.5f;
        [SerializeField] private bool onlyWhenMoving = true;
        [SerializeField] private bool ownerOnly = true;
        [SerializeField] private bool autoFindReferences = true;

        [Header("Frame Pacing Test")]
        [SerializeField] private bool forceStableFramePacingForTest = false;
        [SerializeField, Min(30)] private int forcedTargetFps = 60;
        [SerializeField] private bool disableVsyncForTest = true;

        [Header("Thresholds")]
        [SerializeField, Min(0f)] private float mouseStillThreshold = 0.04f;
        [SerializeField, Min(0f)] private float cameraJitterDegThreshold = 0.18f;
        [SerializeField, Min(0f)] private float cameraPosJitterThreshold = 0.0035f;
        [SerializeField, Min(0f)] private float cameraSpeedJitterThreshold = 1.2f;
        [SerializeField, Min(0f)] private float viewModelJitterThreshold = 0.20f;
        [SerializeField, Range(0.05f, 1f)] private float inputSamplingJitterRatio = 0.35f;

        private Vector2 _prevLookInput;
        private bool _hasPrevLookInput;

        private Quaternion _prevCameraRot;
        private Vector3 _prevCameraPos;
        private Quaternion _prevViewModelRot;
        private bool _hasPrev;

        private float _windowTimer;
        private int _sampleCount;
        private float _sumMouseDelta;
        private float _sumMouseJitter;
        private float _sumMouseSpeed;
        private float _sumMouseSpeedJitter;
        private float _minDt = float.MaxValue;
        private float _maxDt;
        private float _sumCamRotDelta;
        private float _sumCamPosDelta;
        private float _sumCamRotJitter;
        private float _sumCamPosJitter;
        private float _sumCamSpeed;
        private float _sumCamSpeedJitter;
        private float _sumDt;
        private float _sumDtJitter;
        private float _sumViewModelRotDelta;
        private float _sumViewModelRotJitter;
        private float _sumRecoilPatternDelta;
        private float _sumRecoilAnimRot;
        private float _sumRecoilAnimLoc;

        private float _prevCamRotStep;
        private float _prevCamPosStep;
        private float _prevCamSpeed;
        private float _prevDt;
        private float _prevVmRotStep;
        private float _prevMouseStep;
        private float _prevMouseSpeed;
        private bool _hasPrevStep;

        private int _cachedTargetFrameRate = -1;
        private int _cachedVsyncCount = -1;
        private bool _framePacingOverrideApplied;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ApplyFramePacingOverrideIfNeeded();
        }

        private void OnDisable()
        {
            RestoreFramePacingOverride();
        }

        private void OnDestroy()
        {
            RestoreFramePacingOverride();
        }

        private void LateUpdate()
        {
            if (autoFindReferences)
            {
                ResolveReferences();
            }

            ApplyFramePacingOverrideIfNeeded();

            if (!CanSample())
            {
                ResetFrameBaseline();
                return;
            }

            Quaternion camRot = targetCamera.transform.rotation;
            Vector3 camPos = targetCamera.transform.position;
            Quaternion vmRot = viewModelRoot != null ? viewModelRoot.rotation : Quaternion.identity;

            if (!_hasPrev)
            {
                _prevCameraRot = camRot;
                _prevCameraPos = camPos;
                _prevViewModelRot = vmRot;
                _hasPrev = true;
                return;
            }

            bool moving = ReadMovingState();
            if (onlyWhenMoving && !moving)
            {
                _prevCameraRot = camRot;
                _prevCameraPos = camPos;
                _prevViewModelRot = vmRot;
                return;
            }

            float mouseDelta = ReadLookDeltaMagnitude();
            float camRotDelta = Quaternion.Angle(_prevCameraRot, camRot);
            float camPosDelta = Vector3.Distance(_prevCameraPos, camPos);
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            float camSpeed = camPosDelta / dt;
            // 마우스 델타를 dt로 나눈 값(=손 움직임 속도). 입력이 프레임 간격에 정확히 비례해서
            // 들어오면 이 값은 일정해야 한다. 여기가 출렁이면 프레임 페이싱이 아니라
            // 입력 샘플링(마우스 폴링레이트 vs 프레임레이트) 쪽 문제다.
            float mouseSpeed = mouseDelta / dt;
            float vmRotDelta = viewModelRoot != null ? Quaternion.Angle(_prevViewModelRot, vmRot) : 0f;
            float recoilPatternDelta = recoilPattern != null ? recoilPattern.GetRecoilDelta().magnitude : 0f;
            float recoilAnimRot = ReadRecoilAnimationRotMagnitude();
            float recoilAnimLoc = ReadRecoilAnimationLocMagnitude();
            float camRotJitter = 0f;
            float camPosJitter = 0f;
            float camSpeedJitter = 0f;
            float dtJitter = 0f;
            float vmRotJitter = 0f;
            float mouseJitter = 0f;
            float mouseSpeedJitter = 0f;

            if (_hasPrevStep)
            {
                camRotJitter = Mathf.Abs(camRotDelta - _prevCamRotStep);
                camPosJitter = Mathf.Abs(camPosDelta - _prevCamPosStep);
                camSpeedJitter = Mathf.Abs(camSpeed - _prevCamSpeed);
                dtJitter = Mathf.Abs(dt - _prevDt);
                vmRotJitter = Mathf.Abs(vmRotDelta - _prevVmRotStep);
                mouseJitter = Mathf.Abs(mouseDelta - _prevMouseStep);
                mouseSpeedJitter = Mathf.Abs(mouseSpeed - _prevMouseSpeed);
            }

            _prevCamRotStep = camRotDelta;
            _prevCamPosStep = camPosDelta;
            _prevCamSpeed = camSpeed;
            _prevDt = dt;
            _prevVmRotStep = vmRotDelta;
            _prevMouseStep = mouseDelta;
            _prevMouseSpeed = mouseSpeed;
            _hasPrevStep = true;

            _minDt = Mathf.Min(_minDt, dt);
            _maxDt = Mathf.Max(_maxDt, dt);

            _sampleCount++;
            _sumMouseDelta += mouseDelta;
            _sumMouseJitter += mouseJitter;
            _sumMouseSpeed += mouseSpeed;
            _sumMouseSpeedJitter += mouseSpeedJitter;
            _sumCamRotDelta += camRotDelta;
            _sumCamPosDelta += camPosDelta;
            _sumCamRotJitter += camRotJitter;
            _sumCamPosJitter += camPosJitter;
            _sumCamSpeed += camSpeed;
            _sumCamSpeedJitter += camSpeedJitter;
            _sumDt += dt;
            _sumDtJitter += dtJitter;
            _sumViewModelRotDelta += vmRotDelta;
            _sumViewModelRotJitter += vmRotJitter;
            _sumRecoilPatternDelta += recoilPatternDelta;
            _sumRecoilAnimRot += recoilAnimRot;
            _sumRecoilAnimLoc += recoilAnimLoc;

            _windowTimer += Time.unscaledDeltaTime;
            if (_windowTimer >= reportInterval)
            {
                EmitReport();
                ResetWindow();
            }

            _prevCameraRot = camRot;
            _prevCameraPos = camPos;
            _prevViewModelRot = vmRot;
        }

        [ContextMenu("Diagnostics/Report Now")]
        public void ReportNow()
        {
            EmitReport();
        }

        [ContextMenu("Diagnostics/Enable Frame Pacing Test (60)")]
        public void EnableFramePacingTest60()
        {
            forceStableFramePacingForTest = true;
            forcedTargetFps = 60;
            disableVsyncForTest = true;
            ApplyFramePacingOverrideIfNeeded();
        }

        [ContextMenu("Diagnostics/Disable Frame Pacing Test")]
        public void DisableFramePacingTest()
        {
            forceStableFramePacingForTest = false;
            RestoreFramePacingOverride();
        }

        private bool CanSample()
        {
            if (targetCamera == null)
            {
                return false;
            }

            if (!ownerOnly)
            {
                return true;
            }

            if (fpsController == null)
            {
                return true;
            }

            return ReadOwnerState();
        }

        private void ResolveReferences()
        {
            if (fpsController == null)
            {
                fpsController = GetComponentInParent<FPSController>();
            }

            if (movement == null)
            {
                movement = GetComponentInParent<FPSMovement>();
            }

            if (lookController == null)
            {
                lookController = GetComponentInParent<FPSLookController>();
            }

            if (recoilAnimation == null)
            {
                recoilAnimation = GetComponentInParent<RecoilAnimation>();
            }

            if (recoilPattern == null)
            {
                recoilPattern = GetComponentInParent<RecoilPattern>();
            }

            if (targetCamera == null)
            {
                targetCamera = GetComponentInChildren<Camera>(true);
            }

            if (viewModelRoot == null)
            {
                ResolveViewModelRoot();
            }
        }

        private bool ReadOwnerState()
        {
            if (fpsController == null)
            {
                return true;
            }

            object value = ReadMemberValue(fpsController, "isOwner");
            if (value is bool owner)
            {
                return owner;
            }

            return true;
        }

        private bool ReadMovingState()
        {
            if (movement == null)
            {
                return false;
            }

            object value = ReadMemberValue(movement, "IsMoving");
            if (value is bool moving)
            {
                return moving;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo method = movement.GetType().GetMethod("IsMoving", flags, null, Type.EmptyTypes, null);
            if (method != null)
            {
                object result = method.Invoke(movement, null);
                if (result is bool methodMoving)
                {
                    return methodMoving;
                }
            }

            return false;
        }

        private float ReadLookDeltaMagnitude()
        {
            if (lookController == null)
            {
                return 0f;
            }

            object inputValue = ReadMemberValue(lookController, "PlayerInput");
            if (!(inputValue is Vector2 current))
            {
                return 0f;
            }
            if (!_hasPrevLookInput)
            {
                _prevLookInput = current;
                _hasPrevLookInput = true;
                return 0f;
            }

            float delta = (current - _prevLookInput).magnitude;
            _prevLookInput = current;
            return delta;
        }

        private void ResolveViewModelRoot()
        {
            if (recoilAnimation != null)
            {
                Transform parent = recoilAnimation.transform;
                Transform explicitCandidate = FindNamedChild(parent, "WeaponBoneAdditive");
                if (explicitCandidate != null)
                {
                    viewModelRoot = explicitCandidate;
                    return;
                }
            }

            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                string n = children[i].name;
                if (n.IndexOf("weaponbone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("arm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("view", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    viewModelRoot = children[i];
                    return;
                }
            }

            if (recoilAnimation != null)
            {
                viewModelRoot = recoilAnimation.transform;
            }
        }

        private static Transform FindNamedChild(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrEmpty(targetName))
            {
                return null;
            }

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (string.Equals(children[i].name, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return children[i];
                }
            }

            return null;
        }

        private float ReadRecoilAnimationRotMagnitude()
        {
            object value = ReadMemberValue(recoilAnimation, "OutRot");
            if (value is Quaternion q)
            {
                float lengthSquared = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
                if (lengthSquared < 0.000001f)
                {
                    return 0f;
                }

                return Quaternion.Angle(Quaternion.identity, q);
            }

            return 0f;
        }

        private float ReadRecoilAnimationLocMagnitude()
        {
            object value = ReadMemberValue(recoilAnimation, "OutLoc");
            if (value is Vector3 v)
            {
                return v.magnitude;
            }

            return 0f;
        }

        private static object ReadMemberValue(Component target, string memberName)
        {
            if (target == null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            Type t = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            PropertyInfo property = t.GetProperty(memberName, flags);
            if (property != null)
            {
                return property.GetValue(target);
            }

            FieldInfo field = t.GetField(memberName, flags);
            if (field != null)
            {
                return field.GetValue(target);
            }

            return null;
        }

        private void EmitReport()
        {
            float count = Mathf.Max(1, _sampleCount);
            float avgMouse = _sumMouseDelta / count;
            float avgMouseJitter = _sumMouseJitter / count;
            float avgMouseSpeed = _sumMouseSpeed / count;
            float avgMouseSpeedJitter = _sumMouseSpeedJitter / count;
            float avgCamRot = _sumCamRotDelta / count;
            float avgCamPos = _sumCamPosDelta / count;
            float avgCamRotJitter = _sumCamRotJitter / count;
            float avgCamPosJitter = _sumCamPosJitter / count;
            float avgCamSpeed = _sumCamSpeed / count;
            float avgCamSpeedJitter = _sumCamSpeedJitter / count;
            float avgDt = _sumDt / count;
            float avgDtJitter = _sumDtJitter / count;
            float avgVmRot = _sumViewModelRotDelta / count;
            float avgVmRotJitter = _sumViewModelRotJitter / count;
            float avgRecoilPattern = _sumRecoilPatternDelta / count;
            float avgRecoilAnimRot = _sumRecoilAnimRot / count;
            float avgRecoilAnimLoc = _sumRecoilAnimLoc / count;

            bool mouseStill = avgMouse <= mouseStillThreshold;
            bool cameraShaky = avgCamRot >= cameraJitterDegThreshold ||
                              avgCamPosJitter >= cameraPosJitterThreshold ||
                              avgCamSpeedJitter >= cameraSpeedJitterThreshold;
            bool framePacingUnstable = avgDtJitter >= 0.0015f;
            bool recoilActive = avgRecoilPattern > 0.003f || avgRecoilAnimRot > 0.08f || avgRecoilAnimLoc > 0.0008f;
            bool viewModelDominant = avgVmRotJitter >= Mathf.Max(viewModelJitterThreshold, avgCamRotJitter * 1.25f);
            // 마우스 델타가 dt에 비례하지 않고 자체적으로 출렁이면 입력 샘플링 앨리어싱이다.
            bool inputSamplingUneven = avgMouseSpeed > 1f &&
                                       avgMouseSpeedJitter >= avgMouseSpeed * inputSamplingJitterRatio;
            bool steadyMoveNoJitter = mouseStill && avgCamPos > 0.01f && avgCamPosJitter < cameraPosJitterThreshold && avgCamSpeedJitter < cameraSpeedJitterThreshold && avgCamRotJitter < 0.03f;

            string cause;
            if (cameraShaky && mouseStill && recoilActive)
            {
                cause = "LIKELY: recoil/shake additive chain";
            }
            else if (steadyMoveNoJitter)
            {
                cause = "LIKELY: movement is steady (not camera jitter)";
            }
            else if (cameraShaky && mouseStill && framePacingUnstable)
            {
                cause = "LIKELY: frame pacing jitter (Editor/performance)";
            }
            else if (cameraShaky && mouseStill && viewModelDominant)
            {
                cause = "LIKELY: arm/viewmodel animation jitter";
            }
            else if (cameraShaky && mouseStill)
            {
                cause = "LIKELY: camera composition/update-order jitter";
            }
            else if (!cameraShaky && viewModelDominant)
            {
                cause = "LIKELY: camera stable, arm-only jitter";
            }
            else if (!mouseStill && inputSamplingUneven && !framePacingUnstable)
            {
                cause = "LIKELY: mouse input sampling aliasing (delta not proportional to dt)";
            }
            else if (!mouseStill && inputSamplingUneven)
            {
                cause = "LIKELY: input sampling aliasing + frame pacing (both)";
            }
            else
            {
                cause = "UNCLASSIFIED (fallback bucket - do not read as a verdict)";
            }

            string ownerState = fpsController == null ? "UnknownOwner" : (ReadOwnerState() ? "Owner" : "Observer");
            Debug.Log(
                "[CameraJitterDiagnostics] " + ownerState +
                " samples=" + _sampleCount +
                " avgMouse=" + avgMouse.ToString("F4") +
                " avgMouseJitter=" + avgMouseJitter.ToString("F4") +
                " avgMouseSpeed=" + avgMouseSpeed.ToString("F2") +
                " avgMouseSpeedJitter=" + avgMouseSpeedJitter.ToString("F2") +
                " avgCamRotDeg=" + avgCamRot.ToString("F4") +
                " avgCamPos=" + avgCamPos.ToString("F5") +
                " avgCamRotJitter=" + avgCamRotJitter.ToString("F4") +
                " avgCamPosJitter=" + avgCamPosJitter.ToString("F5") +
                " avgCamSpeed=" + avgCamSpeed.ToString("F3") +
                " avgCamSpeedJitter=" + avgCamSpeedJitter.ToString("F3") +
                " avgDt=" + avgDt.ToString("F4") +
                " avgDtJitter=" + avgDtJitter.ToString("F4") +
                " dtMin=" + (_sampleCount > 0 ? _minDt : 0f).ToString("F4") +
                " dtMax=" + _maxDt.ToString("F4") +
                " forceStable=" + forceStableFramePacingForTest +
                " fpsTarget=" + Application.targetFrameRate +
                " vSync=" + QualitySettings.vSyncCount +
                " avgViewModelRotDeg=" + avgVmRot.ToString("F4") +
                " avgViewModelRotJitter=" + avgVmRotJitter.ToString("F4") +
                " avgRecoilPattern=" + avgRecoilPattern.ToString("F4") +
                " avgRecoilAnimRot=" + avgRecoilAnimRot.ToString("F4") +
                " avgRecoilAnimLoc=" + avgRecoilAnimLoc.ToString("F5") +
                " -> " + cause,
                this);
        }

        private void ApplyFramePacingOverrideIfNeeded()
        {
            if (!forceStableFramePacingForTest)
            {
                if (_framePacingOverrideApplied)
                {
                    RestoreFramePacingOverride();
                }

                return;
            }

            if (_framePacingOverrideApplied)
            {
                return;
            }

            _cachedTargetFrameRate = Application.targetFrameRate;
            _cachedVsyncCount = QualitySettings.vSyncCount;

            if (disableVsyncForTest)
            {
                QualitySettings.vSyncCount = 0;
            }

            Application.targetFrameRate = Mathf.Max(30, forcedTargetFps);
            _framePacingOverrideApplied = true;
        }

        private void RestoreFramePacingOverride()
        {
            if (!_framePacingOverrideApplied)
            {
                return;
            }

            Application.targetFrameRate = _cachedTargetFrameRate;
            if (_cachedVsyncCount >= 0)
            {
                QualitySettings.vSyncCount = _cachedVsyncCount;
            }

            _framePacingOverrideApplied = false;
            _cachedTargetFrameRate = -1;
            _cachedVsyncCount = -1;
        }

        private void ResetWindow()
        {
            _windowTimer = 0f;
            _sampleCount = 0;
            _sumMouseDelta = 0f;
            _sumMouseJitter = 0f;
            _sumMouseSpeed = 0f;
            _sumMouseSpeedJitter = 0f;
            _minDt = float.MaxValue;
            _maxDt = 0f;
            _sumCamRotDelta = 0f;
            _sumCamPosDelta = 0f;
            _sumCamRotJitter = 0f;
            _sumCamPosJitter = 0f;
            _sumCamSpeed = 0f;
            _sumCamSpeedJitter = 0f;
            _sumDt = 0f;
            _sumDtJitter = 0f;
            _sumViewModelRotDelta = 0f;
            _sumViewModelRotJitter = 0f;
            _sumRecoilPatternDelta = 0f;
            _sumRecoilAnimRot = 0f;
            _sumRecoilAnimLoc = 0f;
        }

        private void ResetFrameBaseline()
        {
            if (targetCamera == null)
            {
                _hasPrev = false;
                return;
            }

            _prevCameraRot = targetCamera.transform.rotation;
            _prevCameraPos = targetCamera.transform.position;
            _prevViewModelRot = viewModelRoot != null ? viewModelRoot.rotation : Quaternion.identity;
            _hasPrev = true;
            _hasPrevStep = false;
        }
    }
}
