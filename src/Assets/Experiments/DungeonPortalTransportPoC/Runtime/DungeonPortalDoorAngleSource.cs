using System;
using UnityEngine;

namespace DungeonPortalTransportPoC
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class DungeonPortalDoorAngleSource : MonoBehaviour
    {
        [SerializeField] private Transform doorLeaf;
        [SerializeField] private bool captureClosedRotationOnAwake = true;
        [SerializeField] private Quaternion closedLocalRotation = Quaternion.identity;
        [SerializeField] private Vector3 localHingeAxis = Vector3.up;
        [SerializeField, Range(-90f, 90f)] private float openAngleDegrees = 90f;
        [SerializeField, Min(0f)] private float notificationThreshold = 0.001f;

        private float lastNotifiedOpenFraction = -1f;

        public event Action<float, float> ApertureChanged;

        public float OpenFraction { get; private set; }
        public float ApertureFraction { get; private set; }
        public Transform DoorLeaf => doorLeaf;
        public bool IsConfigured =>
            doorLeaf != null &&
            localHingeAxis.sqrMagnitude > Mathf.Epsilon &&
            Mathf.Abs(openAngleDegrees) > 0.001f;

        public void Configure(
            Transform movingDoorLeaf,
            Quaternion closedRotation,
            Vector3 hingeAxis,
            float fullOpenAngleDegrees)
        {
            doorLeaf = movingDoorLeaf;
            closedLocalRotation = closedRotation;
            localHingeAxis = hingeAxis.sqrMagnitude > Mathf.Epsilon
                ? hingeAxis.normalized
                : Vector3.up;
            openAngleDegrees = Mathf.Clamp(fullOpenAngleDegrees, -90f, 90f);
            captureClosedRotationOnAwake = false;
            EvaluateNow();
        }

        public void CaptureCurrentRotationAsClosed()
        {
            if (doorLeaf == null)
                return;

            closedLocalRotation = doorLeaf.localRotation;
            lastNotifiedOpenFraction = -1f;
            EvaluateNow();
        }

        private void Awake()
        {
            if (captureClosedRotationOnAwake && doorLeaf != null)
                closedLocalRotation = doorLeaf.localRotation;

            EvaluateNow();
        }

        private void LateUpdate()
        {
            EvaluateNow();
        }

        public void EvaluateNow()
        {
            float nextOpenFraction = doorLeaf != null
                ? PortalTransportMath.ComputeDoorOpenFraction(
                    closedLocalRotation,
                    doorLeaf.localRotation,
                    localHingeAxis,
                    openAngleDegrees)
                : 0f;
            float nextApertureFraction = PortalTransportMath.ComputeProjectedApertureFraction(
                nextOpenFraction,
                openAngleDegrees);

            OpenFraction = nextOpenFraction;
            ApertureFraction = nextApertureFraction;

            if (lastNotifiedOpenFraction >= 0f &&
                Mathf.Abs(OpenFraction - lastNotifiedOpenFraction) < notificationThreshold)
            {
                return;
            }

            lastNotifiedOpenFraction = OpenFraction;
            ApertureChanged?.Invoke(OpenFraction, ApertureFraction);
        }

        private void OnValidate()
        {
            if (localHingeAxis.sqrMagnitude <= Mathf.Epsilon)
                localHingeAxis = Vector3.up;
            else
                localHingeAxis.Normalize();

            openAngleDegrees = Mathf.Clamp(openAngleDegrees, -90f, 90f);
            notificationThreshold = Mathf.Max(0f, notificationThreshold);
        }
    }
}
