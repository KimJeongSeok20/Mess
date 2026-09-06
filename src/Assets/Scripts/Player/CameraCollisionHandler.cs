// CameraCollisionHandler.cs
// Dynamically expands CharacterController envelope based on look pitch near walls.

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Demo.Scripts.Runtime.Character
{
    public class CameraCollisionHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CharacterController characterController;

        [Header("Head Probe")]
        [SerializeField, Range(0.05f, 0.4f)] private float probeRadius = 0.14f;
        [SerializeField, Range(0.1f, 1.5f)] private float probeDistance = 0.7f;
        [SerializeField, Range(0.1f, 1f)] private float activationDistance = 0.45f;
        [SerializeField] private LayerMask collisionLayers = ~0;
        [SerializeField] private bool checkPlayerBody = false;

        [Header("Controller Envelope")]
        [SerializeField, Range(0f, 0.3f)] private float maxForwardCenterOffset = 0.16f;
        [SerializeField, Range(0f, 0.2f)] private float maxRadiusIncrease = 0.08f;
        [SerializeField, Range(1f, 90f)] private float fullEffectPitch = 60f;
        [SerializeField, Range(1f, 40f)] private float envelopeSmoothing = 16f;

        [Header("Pose Forward Alignment")]
        [SerializeField, Range(-0.25f, 0.25f)] private float standingForwardOffset = 0f;
        [SerializeField, Range(-0.25f, 0.25f)] private float crouchingForwardOffset = 0f;
        [SerializeField, Range(-0.25f, 0.25f)] private float proneForwardOffset = 0f;

        [Header("Debug")]
        [SerializeField] private bool showDebugGizmos = false;
        [SerializeField] private bool showControllerEnvelopeGizmo = true;
        [SerializeField] private bool showDebugLabels = true;
        [SerializeField] private bool logCollisions = false;

        private Transform _playerRoot;
        private FPSMovement _movement;
        private bool _isInitialized;
        private int _worldMask;

        private float _appliedPoseForwardOffset;
        private float _appliedForwardOffset;
        private float _appliedRadiusIncrease;

        private float _lastBaseCenterZ;
        private float _lastBaseRadius;
        private bool _hasBaseSnapshot;

        private Vector3 _lastProbeDirection = Vector3.forward;
        private bool _hadBarrierLastFrame;
        private float _lastNearestDistance = float.PositiveInfinity;

        private readonly RaycastHit[] _castResults = new RaycastHit[24];
        private static readonly Vector3[] _probeLateralOffsets =
        {
            Vector3.zero,
            new Vector3(0.2f, 0f, 0f),
            new Vector3(-0.2f, 0f, 0f)
        };

        private void Start()
        {
            Initialize();
        }

        private void OnEnable()
        {
            _isInitialized = false;
            _appliedPoseForwardOffset = 0f;
            _appliedForwardOffset = 0f;
            _appliedRadiusIncrease = 0f;
            _hasBaseSnapshot = false;
        }

        private void OnDisable()
        {
            ResetEnvelope();
        }

        private void Initialize()
        {
            if (_isInitialized) return;

            if (characterController == null)
            {
                characterController = GetComponentInParent<CharacterController>();
            }

            _playerRoot = characterController != null ? characterController.transform : transform.root;
            _movement = characterController != null
                ? characterController.GetComponent<FPSMovement>()
                : GetComponentInParent<FPSMovement>();
            _worldMask = collisionLayers;

            int ignoreLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreLayer >= 0)
            {
                _worldMask &= ~(1 << ignoreLayer);
            }

            _isInitialized = true;
        }

        public void ApplyLookDrivenControllerEnvelope(Transform playerTransform, float lookPitch)
        {
            if (playerTransform == null) return;

            if (!_isInitialized)
            {
                Initialize();
            }

            if (characterController == null) return;

            float previousPoseForwardOffset = _appliedPoseForwardOffset;
            float previousForwardOffset = _appliedForwardOffset;
            float previousRadiusIncrease = _appliedRadiusIncrease;
            float previousTotalForwardOffset = previousPoseForwardOffset + previousForwardOffset;

            float targetWeight = 0f;
            _hadBarrierLastFrame = false;
            _lastNearestDistance = float.PositiveInfinity;

            float lookDownWeight = Mathf.Clamp01(lookPitch / Mathf.Max(1f, fullEffectPitch));
            if (lookDownWeight > 0f && TryGetForwardBarrierDistance(playerTransform, out float nearestDistance))
            {
                float distanceWeight = Mathf.Clamp01((activationDistance - nearestDistance) / Mathf.Max(0.001f, activationDistance));
                targetWeight = lookDownWeight * distanceWeight;
                _hadBarrierLastFrame = targetWeight > 0f;
                _lastNearestDistance = nearestDistance;
            }

            float targetForwardOffset = maxForwardCenterOffset * targetWeight;
            float targetRadiusIncrease = maxRadiusIncrease * targetWeight;
            float targetPoseForwardOffset = GetTargetPoseForwardOffset();

            float alpha;
            if (!Application.isPlaying)
            {
                alpha = 1f;
            }
            else
            {
                alpha = 1f - Mathf.Exp(-envelopeSmoothing * Time.deltaTime);
            }

            _appliedPoseForwardOffset = Mathf.Lerp(previousPoseForwardOffset, targetPoseForwardOffset, alpha);
            _appliedForwardOffset = Mathf.Lerp(previousForwardOffset, targetForwardOffset, alpha);
            _appliedRadiusIncrease = Mathf.Lerp(previousRadiusIncrease, targetRadiusIncrease, alpha);

            float nextTotalForwardOffset = _appliedPoseForwardOffset + _appliedForwardOffset;

            ApplyEnvelope(previousTotalForwardOffset, previousRadiusIncrease, nextTotalForwardOffset);

            if (logCollisions && _hadBarrierLastFrame)
            {
                Debug.Log(
                    $"[CameraCollision] envelope applied pitch={lookPitch:F1}, distance={_lastNearestDistance:F3}, " +
                    $"centerZ={nextTotalForwardOffset:F3} (pose={_appliedPoseForwardOffset:F3}, look={_appliedForwardOffset:F3}), " +
                    $"radius+={_appliedRadiusIncrease:F3}");
            }
        }

        private float GetTargetPoseForwardOffset()
        {
            if (_movement == null)
            {
                return standingForwardOffset;
            }

            return _movement.PoseState switch
            {
                FPSPoseState.Crouching => crouchingForwardOffset,
                FPSPoseState.Prone => proneForwardOffset,
                _ => standingForwardOffset
            };
        }

        public void SetCharacterController(CharacterController cc)
        {
            ResetEnvelope();
            characterController = cc;
            _movement = characterController != null ? characterController.GetComponent<FPSMovement>() : null;
            _isInitialized = false;
            _hasBaseSnapshot = false;
        }

        public void ResetEnvelope()
        {
            if (!_isInitialized)
            {
                return;
            }

            if (characterController == null)
            {
                _appliedPoseForwardOffset = 0f;
                _appliedForwardOffset = 0f;
                _appliedRadiusIncrease = 0f;
                _hasBaseSnapshot = false;
                return;
            }

            float previousPoseForwardOffset = _appliedPoseForwardOffset;
            float previousForwardOffset = _appliedForwardOffset;
            float previousRadiusIncrease = _appliedRadiusIncrease;
            float previousTotalForwardOffset = previousPoseForwardOffset + previousForwardOffset;

            _appliedPoseForwardOffset = 0f;
            _appliedForwardOffset = 0f;
            _appliedRadiusIncrease = 0f;

            ApplyEnvelope(previousTotalForwardOffset, previousRadiusIncrease, 0f);
            _hasBaseSnapshot = false;
        }

        private void ApplyEnvelope(float previousForwardOffset, float previousRadiusIncrease, float nextForwardOffset)
        {
            Vector3 currentCenter = characterController.center;
            float currentRadius = characterController.radius;

            bool canRemovePreviousCenterOffset = _hasBaseSnapshot
                && Mathf.Abs(currentCenter.z - (_lastBaseCenterZ + previousForwardOffset)) <= 0.01f;
            bool canRemovePreviousRadiusOffset = _hasBaseSnapshot
                && Mathf.Abs(currentRadius - (_lastBaseRadius + previousRadiusIncrease)) <= 0.01f;

            Vector3 baseCenter = currentCenter;
            if (canRemovePreviousCenterOffset)
            {
                baseCenter.z -= previousForwardOffset;
            }

            float baseRadius = canRemovePreviousRadiusOffset
                ? Mathf.Max(0.01f, currentRadius - previousRadiusIncrease)
                : Mathf.Max(0.01f, currentRadius);

            float maxAllowedRadius = Mathf.Max(0.01f, characterController.height * 0.5f - 0.001f);
            float nextRadius = Mathf.Min(baseRadius + _appliedRadiusIncrease, maxAllowedRadius);

            baseCenter.z += nextForwardOffset;

            // CharacterController의 radius/center에 값을 쓰면 내부 PhysX 캡슐이 다시 만들어지고
            // 접지 상태(isGrounded)가 초기화된다. 이 메서드는 FPSController.Update()에서 매 프레임
            // 호출되는데, 그 Update가 FPSMovement.Update()의 Move()보다 뒤에 실행되면 방금 갱신된
            // isGrounded가 지워져 캐릭터가 영구히 InAir 상태로 굳는다(= 이동 불가).
            // 두 스크립트 모두 실행 순서가 0이라 순서는 재컴파일마다 바뀔 수 있으므로,
            // 실제로 값이 달라졌을 때만 써서 순서에 의존하지 않게 만든다.
            if (!Mathf.Approximately(characterController.radius, nextRadius))
            {
                characterController.radius = nextRadius;
            }

            if ((characterController.center - baseCenter).sqrMagnitude > 1e-10f)
            {
                characterController.center = baseCenter;
            }

            _lastBaseCenterZ = baseCenter.z - nextForwardOffset;
            _lastBaseRadius = nextRadius - _appliedRadiusIncrease;
            _hasBaseSnapshot = true;
        }

        private bool TryGetForwardBarrierDistance(Transform playerTransform, out float nearestDistance)
        {
            Vector3 direction = playerTransform.forward;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
            {
                nearestDistance = float.PositiveInfinity;
                return false;
            }

            direction.Normalize();
            _lastProbeDirection = direction;

            Vector3 origin = transform.position;
            Vector3 right = playerTransform.right;
            right.y = 0f;

            if (right.sqrMagnitude > 0.0001f)
            {
                right.Normalize();
            }
            else
            {
                right = Vector3.right;
            }

            bool found = false;
            float nearest = float.PositiveInfinity;

            for (int i = 0; i < _probeLateralOffsets.Length; i++)
            {
                Vector3 probeOrigin = origin + right * (_probeLateralOffsets[i].x * probeRadius);
                int hitCount = Physics.SphereCastNonAlloc(
                    probeOrigin,
                    probeRadius * 0.8f,
                    direction,
                    _castResults,
                    probeDistance,
                    _worldMask,
                    QueryTriggerInteraction.Ignore);

                for (int j = 0; j < hitCount; j++)
                {
                    Collider hitCollider = _castResults[j].collider;
                    if (!IsBlockingCollider(hitCollider)) continue;

                    float distance = _castResults[j].distance;
                    if (distance < nearest)
                    {
                        nearest = distance;
                        found = true;
                    }
                }
            }

            nearestDistance = nearest;
            return found;
        }

        private bool IsBlockingCollider(Collider col)
        {
            if (col == null || !col.enabled || col.isTrigger) return false;

            if (!checkPlayerBody && IsOwnCollider(col))
            {
                return false;
            }

            if (col.transform == transform || col.transform.IsChildOf(transform))
            {
                return false;
            }

            return true;
        }

        private bool IsOwnCollider(Collider col)
        {
            if (col == characterController) return true;
            if (_playerRoot == null) return false;
            return col.transform == _playerRoot || col.transform.IsChildOf(_playerRoot);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!showDebugGizmos) return;

            Gizmos.color = _hadBarrierLastFrame ? Color.red : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, probeRadius);

            Vector3 from = transform.position;
            Vector3 to = from + _lastProbeDirection.normalized * probeDistance;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(from, to);

            if (_hadBarrierLastFrame)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(from + _lastProbeDirection.normalized * _lastNearestDistance, probeRadius * 0.8f);
            }

            if (!showControllerEnvelopeGizmo)
            {
                return;
            }

            CharacterController controller = characterController != null
                ? characterController
                : GetComponentInParent<CharacterController>();

            if (controller == null)
            {
                return;
            }

            DrawCurrentAndBaseControllerCapsules(controller);

            if (!showDebugLabels)
            {
                return;
            }

            Vector3 labelPosition = controller.transform.TransformPoint(controller.center + Vector3.up * (controller.height * 0.5f + 0.2f));
            float totalForwardOffset = _appliedPoseForwardOffset + _appliedForwardOffset;
            string label =
                $"CC radius: {controller.radius:F3}\n" +
                $"CC center.z: {controller.center.z:F3}\n" +
                $"Pose z+: {_appliedPoseForwardOffset:F3} Look z+: {_appliedForwardOffset:F3}\n" +
                $"Total z+: {totalForwardOffset:F3} r+: {_appliedRadiusIncrease:F3}";
            Handles.Label(labelPosition, label);
        }

        private void DrawCurrentAndBaseControllerCapsules(CharacterController controller)
        {
            Vector3 currentCenter = controller.center;
            float currentRadius = controller.radius;
            float totalForwardOffset = _appliedPoseForwardOffset + _appliedForwardOffset;

            DrawCapsule(controller.transform, currentCenter, currentRadius, controller.height, new Color(0.2f, 1f, 0.2f, 0.95f));

            bool hasEnvelope = totalForwardOffset > 0.0001f || _appliedRadiusIncrease > 0.0001f;
            if (!hasEnvelope)
            {
                return;
            }

            Vector3 baseCenter = currentCenter;
            baseCenter.z -= totalForwardOffset;
            float baseRadius = Mathf.Max(0.01f, currentRadius - _appliedRadiusIncrease);

            DrawCapsule(controller.transform, baseCenter, baseRadius, controller.height, new Color(0.9f, 0.75f, 0.15f, 0.95f));

            Vector3 baseCenterWorld = controller.transform.TransformPoint(baseCenter);
            Vector3 currentCenterWorld = controller.transform.TransformPoint(currentCenter);

            Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.95f);
            Gizmos.DrawLine(baseCenterWorld, currentCenterWorld);
        }

        private static void DrawCapsule(Transform targetTransform, Vector3 center, float radius, float height, Color color)
        {
            float capsuleRadius = Mathf.Max(0.001f, radius);
            float capsuleHeight = Mathf.Max(capsuleRadius * 2f, height);
            float halfSegment = capsuleHeight * 0.5f - capsuleRadius;

            Vector3 top = center + Vector3.up * halfSegment;
            Vector3 bottom = center - Vector3.up * halfSegment;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            Gizmos.matrix = targetTransform.localToWorldMatrix;
            Gizmos.color = color;

            Gizmos.DrawWireSphere(top, capsuleRadius);
            Gizmos.DrawWireSphere(bottom, capsuleRadius);

            Gizmos.DrawLine(top + Vector3.right * capsuleRadius, bottom + Vector3.right * capsuleRadius);
            Gizmos.DrawLine(top - Vector3.right * capsuleRadius, bottom - Vector3.right * capsuleRadius);
            Gizmos.DrawLine(top + Vector3.forward * capsuleRadius, bottom + Vector3.forward * capsuleRadius);
            Gizmos.DrawLine(top - Vector3.forward * capsuleRadius, bottom - Vector3.forward * capsuleRadius);

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
#endif
    }
}
