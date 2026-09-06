using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace DungeonPortalTransportPoC.KExactBasisV1
{
    /// <summary>
    /// Assembly-safe scalar adapter. It can be driven explicitly, or can read a public
    /// property/field/zero-argument method on a production MonoBehaviour without taking
    /// a compile-time dependency on Assembly-CSharp.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KExactScalarSource : MonoBehaviour
    {
        public enum SourceMode
        {
            Manual = 0,
            PublicMember = 1
        }

        [SerializeField] private SourceMode mode = SourceMode.Manual;
        [SerializeField, Range(0f, 1f)] private float manualValue01 = 1f;
        [SerializeField] private MonoBehaviour sourceComponent;
        [SerializeField] private string publicMemberName = "Power01";
        [SerializeField] private float memberValueAtZero;
        [SerializeField] private float memberValueAtOne = 1f;
        [SerializeField] private bool invert;

        private Type cachedSourceType;
        private MemberInfo cachedMember;
        private string cachedMemberName;
        private float lastValue01;
        [NonSerialized] private bool hasRuntimeOverride;
        [NonSerialized] private float runtimeOverride01;

        public SourceMode Mode => mode;
        public MonoBehaviour SourceComponent => sourceComponent;
        public string PublicMemberName => publicMemberName;
        public float Value01 => lastValue01;
        public bool HasRuntimeOverride => hasRuntimeOverride;
        public bool IsConfigured => mode == SourceMode.Manual ||
                                    (sourceComponent != null &&
                                     !string.IsNullOrWhiteSpace(publicMemberName));

        public void ConfigureManual(float value01)
        {
            mode = SourceMode.Manual;
            sourceComponent = null;
            publicMemberName = string.Empty;
            invert = false;
            manualValue01 = Mathf.Clamp01(value01);
            lastValue01 = ApplyInvert(manualValue01);
            ClearRuntimeOverride();
            ClearMemberCache();
        }

        public void ConfigureMember(
            MonoBehaviour component,
            string memberName,
            float valueAtZero = 0f,
            float valueAtOne = 1f,
            bool invertValue = false)
        {
            mode = SourceMode.PublicMember;
            sourceComponent = component;
            publicMemberName = memberName ?? string.Empty;
            memberValueAtZero = valueAtZero;
            memberValueAtOne = valueAtOne;
            invert = invertValue;
            ClearRuntimeOverride();
            ClearMemberCache();
        }

        public void SetManualValue(float value01)
        {
            manualValue01 = Mathf.Clamp01(value01);
            if (mode == SourceMode.Manual)
                lastValue01 = ApplyInvert(manualValue01);
        }

        /// <summary>
        /// Non-serialized deterministic override used only by isolated capture tooling.
        /// It never changes the configured production member binding.
        /// </summary>
        public void SetRuntimeOverride01(float value01)
        {
            runtimeOverride01 = Mathf.Clamp01(value01);
            hasRuntimeOverride = true;
            lastValue01 = runtimeOverride01;
        }

        public void ClearRuntimeOverride()
        {
            hasRuntimeOverride = false;
            runtimeOverride01 = 0f;
        }

        public bool TryRead01(out float value01, out string failure)
        {
            if (hasRuntimeOverride)
            {
                value01 = runtimeOverride01;
                lastValue01 = value01;
                failure = null;
                return true;
            }

            if (mode == SourceMode.Manual)
            {
                value01 = ApplyInvert(Mathf.Clamp01(manualValue01));
                lastValue01 = value01;
                failure = null;
                return true;
            }

            if (sourceComponent == null || string.IsNullOrWhiteSpace(publicMemberName))
            {
                value01 = 0f;
                failure = "Public-member scalar source is missing its component or member name.";
                return false;
            }

            if (!TryResolveMember(out failure))
            {
                value01 = 0f;
                return false;
            }

            try
            {
                object raw;
                switch (cachedMember)
                {
                    case PropertyInfo property:
                        raw = property.GetValue(sourceComponent, null);
                        break;
                    case FieldInfo field:
                        raw = field.GetValue(sourceComponent);
                        break;
                    case MethodInfo method:
                        raw = method.Invoke(sourceComponent, null);
                        break;
                    default:
                        value01 = 0f;
                        failure = "Resolved scalar member has an unsupported reflection type.";
                        return false;
                }

                if (!TryConvertTo01(raw, out value01, out failure))
                    return false;

                value01 = ApplyInvert(value01);
                lastValue01 = value01;
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                Exception root = exception is TargetInvocationException invocation &&
                                 invocation.InnerException != null
                    ? invocation.InnerException
                    : exception;
                value01 = 0f;
                failure = $"Could not read '{publicMemberName}' from " +
                          $"'{sourceComponent.GetType().FullName}': " +
                          $"{root.GetType().Name}: {root.Message}";
                return false;
            }
        }

        private bool TryResolveMember(out string failure)
        {
            Type sourceType = sourceComponent.GetType();
            if (cachedMember != null && cachedSourceType == sourceType &&
                string.Equals(cachedMemberName, publicMemberName, StringComparison.Ordinal))
            {
                failure = null;
                return true;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            cachedSourceType = sourceType;
            cachedMemberName = publicMemberName;
            cachedMember = sourceType.GetProperty(publicMemberName, flags);
            if (cachedMember is PropertyInfo property && !property.CanRead)
                cachedMember = null;

            cachedMember ??= sourceType.GetField(publicMemberName, flags);
            cachedMember ??= sourceType.GetMethod(
                publicMemberName,
                flags,
                null,
                Type.EmptyTypes,
                null);

            if (cachedMember == null)
            {
                failure = $"Public scalar member '{publicMemberName}' was not found on " +
                          $"'{sourceType.FullName}'.";
                return false;
            }

            failure = null;
            return true;
        }

        private bool TryConvertTo01(object raw, out float value01, out string failure)
        {
            if (raw == null)
            {
                value01 = 0f;
                failure = $"Scalar member '{publicMemberName}' returned null.";
                return false;
            }

            if (raw is bool boolean)
            {
                value01 = boolean ? 1f : 0f;
                failure = null;
                return true;
            }

            Type valueType = raw.GetType();
            if (valueType.IsEnum)
            {
                string enumName = raw.ToString();
                if (!string.IsNullOrEmpty(enumName))
                {
                    if (enumName.IndexOf("100", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        enumName.Equals("On", StringComparison.OrdinalIgnoreCase))
                    {
                        value01 = 1f;
                        failure = null;
                        return true;
                    }

                    if (enumName.IndexOf("0", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        enumName.Equals("Off", StringComparison.OrdinalIgnoreCase))
                    {
                        value01 = 0f;
                        failure = null;
                        return true;
                    }
                }
            }

            if (!(raw is IConvertible convertible))
            {
                value01 = 0f;
                failure = $"Scalar member '{publicMemberName}' has unsupported type " +
                          $"'{valueType.FullName}'.";
                return false;
            }

            float numeric;
            try
            {
                numeric = convertible.ToSingle(CultureInfo.InvariantCulture);
            }
            catch (Exception exception)
            {
                value01 = 0f;
                failure = $"Scalar member '{publicMemberName}' could not be converted to float: " +
                          exception.Message;
                return false;
            }

            float span = memberValueAtOne - memberValueAtZero;
            if (!IsFinite(numeric) || !IsFinite(span) || Mathf.Abs(span) < 0.000001f)
            {
                value01 = 0f;
                failure = "Scalar normalization endpoints are invalid or the member is non-finite.";
                return false;
            }

            value01 = Mathf.Clamp01((numeric - memberValueAtZero) / span);
            failure = null;
            return true;
        }

        private float ApplyInvert(float value01)
        {
            return invert ? 1f - Mathf.Clamp01(value01) : Mathf.Clamp01(value01);
        }

        private void ClearMemberCache()
        {
            cachedSourceType = null;
            cachedMember = null;
            cachedMemberName = null;
        }

        private void OnValidate()
        {
            manualValue01 = Mathf.Clamp01(manualValue01);
            if (!IsFinite(memberValueAtZero))
                memberValueAtZero = 0f;
            if (!IsFinite(memberValueAtOne) ||
                Mathf.Abs(memberValueAtOne - memberValueAtZero) < 0.000001f)
            {
                memberValueAtOne = memberValueAtZero + 1f;
            }

            ClearMemberCache();
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
