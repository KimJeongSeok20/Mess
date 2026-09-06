using System;
using UnityEngine;

namespace DungeonPortalTransportPoC
{
    [DisallowMultipleComponent]
    public sealed class DungeonPortalPowerEnvelope : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float initialPower01 = 1f;
        [SerializeField, Min(0f)] private float powerOnDuration = 1f;
        [SerializeField, Min(0f)] private float powerOffDuration = 1f;

        private PortalPowerTransition transition;
        private float lastNotifiedValue = -1f;

        public event Action<float> PowerChanged;

        public float Power01 => transition != null ? transition.Current : initialPower01;
        public float TargetPower01 => transition != null ? transition.Target : initialPower01;

        private void Awake()
        {
            transition = new PortalPowerTransition(initialPower01);
            NotifyIfChanged(force: true);
        }

        private void Update()
        {
            if (transition == null)
                return;

            transition.Tick(Time.deltaTime);
            NotifyIfChanged(force: false);
        }

        public void SetPowered(bool powered)
        {
            SetTargetPower(powered ? 1f : 0f);
        }

        public void SetTargetPower(float targetPower01)
        {
            EnsureTransition();
            float clampedTarget = Mathf.Clamp01(targetPower01);
            if (Mathf.Abs(clampedTarget - transition.Target) <= 0.0001f)
                return;

            float duration = clampedTarget >= transition.Current
                ? powerOnDuration
                : powerOffDuration;
            transition.SetTarget(clampedTarget, duration);
            NotifyIfChanged(force: duration <= Mathf.Epsilon);
        }

        public void SetImmediate(float power01)
        {
            EnsureTransition();
            transition.SetTarget(Mathf.Clamp01(power01), 0f);
            NotifyIfChanged(force: true);
        }

        private void EnsureTransition()
        {
            if (transition == null)
                transition = new PortalPowerTransition(initialPower01);
        }

        private void NotifyIfChanged(bool force)
        {
            float value = Power01;
            if (!force && Mathf.Abs(value - lastNotifiedValue) < 0.0001f)
                return;

            lastNotifiedValue = value;
            PowerChanged?.Invoke(value);
        }

        private void OnValidate()
        {
            initialPower01 = Mathf.Clamp01(initialPower01);
            powerOnDuration = Mathf.Max(0f, powerOnDuration);
            powerOffDuration = Mathf.Max(0f, powerOffDuration);
        }
    }
}
