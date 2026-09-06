using UnityEngine;

namespace DungeonPortalTransportPoC
{
    public sealed class PortalPowerTransition
    {
        private float startValue;
        private float targetValue;
        private float duration;
        private float elapsed;

        public PortalPowerTransition(float initialValue)
        {
            Current = Mathf.Clamp01(initialValue);
            startValue = Current;
            targetValue = Current;
        }

        public float Current { get; private set; }
        public float Target => targetValue;
        public bool IsTransitioning => elapsed < duration;

        public void SetTarget(float value, float transitionDuration)
        {
            startValue = Current;
            targetValue = Mathf.Clamp01(value);
            duration = Mathf.Max(0f, transitionDuration);
            elapsed = 0f;

            if (duration <= Mathf.Epsilon)
                Current = targetValue;
        }

        public float Tick(float deltaTime)
        {
            if (!IsTransitioning)
                return Current;

            elapsed = Mathf.Min(duration, elapsed + Mathf.Max(0f, deltaTime));
            float normalizedTime = duration <= Mathf.Epsilon ? 1f : elapsed / duration;
            float easedTime = PortalTransportMath.SmoothStep01(normalizedTime);
            Current = Mathf.LerpUnclamped(startValue, targetValue, easedTime);
            return Current;
        }
    }
}

