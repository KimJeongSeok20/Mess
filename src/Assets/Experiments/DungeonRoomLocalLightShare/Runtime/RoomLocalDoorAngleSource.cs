using System;
using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    /// <summary>
    /// Adapted from DungeonPortalTransportPoC.DungeonPortalDoorAngleSource.
    /// Uses the real leaf rotation, never Door.IsOpen.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class RoomLocalDoorAngleSource : MonoBehaviour
    {
        [SerializeField] private Transform doorLeaf;
        [SerializeField] private bool captureClosedRotationOnAwake = true;
        [SerializeField] private Quaternion closedLocalRotation = Quaternion.identity;
        [SerializeField] private Vector3 localHingeAxis = Vector3.up;
        [SerializeField] private float openAngleDegrees = 90f;

        public float OpenFraction { get; private set; }
        public float ApertureFraction { get; private set; }
        public Transform DoorLeaf => doorLeaf;
        public Quaternion ClosedLocalRotation => closedLocalRotation;
        public Vector3 LocalHingeAxis => localHingeAxis;
        public float OpenAngleDegrees => openAngleDegrees;
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
            EvaluateNow();
        }

        public void ApplyOpenFraction(float openFraction)
        {
            if (doorLeaf == null)
                return;
            doorLeaf.localRotation = RoomLocalLightShareMath.DoorLocalRotationForOpenFraction(
                closedLocalRotation,
                localHingeAxis,
                openAngleDegrees,
                openFraction);
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
            OpenFraction = doorLeaf != null
                ? RoomLocalLightShareMath.ComputeDoorOpenFraction(
                    closedLocalRotation,
                    doorLeaf.localRotation,
                    localHingeAxis,
                    openAngleDegrees)
                : 0f;
            ApertureFraction = RoomLocalLightShareMath.ComputeProjectedApertureFraction(
                OpenFraction,
                openAngleDegrees);
        }
    }
}
