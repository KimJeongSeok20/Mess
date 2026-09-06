using System;
using System.Collections.Generic;
using UnityEngine;

namespace DungeonPortalTransportPoC.KExactBasisV1
{
    [DefaultExecutionOrder(-70)]
    [DisallowMultipleComponent]
    public sealed class KExactPortalRuntimeManager : MonoBehaviour
    {
        [SerializeField] private KExactPortalConnection[] connections =
            Array.Empty<KExactPortalConnection>();
        [SerializeField] private bool activateOnStart = true;

        private bool active;
        private bool faultLatched;
        private string faultReason;

        public bool IsActive => active;
        public bool IsFaultLatched => faultLatched;
        public string FaultReason => faultReason;
        public KExactPortalConnection[] Connections => CopyArray(connections);

        public void Configure(KExactPortalConnection[] managedConnections)
        {
            DeactivateAll();
            connections = CopyArray(managedConnections);
            ResetFault();
        }

        public bool TryActivateAll(out string failure)
        {
            if (!Application.isPlaying)
            {
                failure = "K-exact runtime manager activates only in Play Mode.";
                return false;
            }

            if (active)
            {
                failure = null;
                return true;
            }

            if (faultLatched)
            {
                failure = "Runtime manager is fault-latched. Call ResetFault before retrying: " +
                          faultReason;
                return false;
            }

            if (!TryValidateConnections(out failure))
            {
                LatchFault(failure);
                return false;
            }

            var activated = new List<KExactPortalConnection>(connections.Length);
            for (int i = 0; i < connections.Length; i++)
            {
                KExactPortalConnection connection = connections[i];
                connection.SetActivateOnEnable(false);
                if (connection.TryActivate(out failure))
                {
                    activated.Add(connection);
                    continue;
                }

                for (int rollback = activated.Count - 1; rollback >= 0; rollback--)
                    activated[rollback].Deactivate();
                failure = $"Connection index {i} ('{connection.ConnectionKey}') failed: {failure}";
                LatchFault(failure);
                return false;
            }

            active = true;
            failure = null;
            return true;
        }

        public void DeactivateAll()
        {
            if (connections != null)
            {
                for (int i = connections.Length - 1; i >= 0; i--)
                {
                    if (connections[i] != null)
                        connections[i].Deactivate();
                }
            }

            active = false;
        }

        public void ResetFault()
        {
            if (active)
                return;
            faultLatched = false;
            faultReason = null;
            if (connections == null)
                return;
            for (int i = 0; i < connections.Length; i++)
                connections[i]?.ResetFault();
        }

        private void Start()
        {
            if (!activateOnStart)
                return;
            if (!TryActivateAll(out string failure))
                Debug.LogError($"[{nameof(KExactPortalRuntimeManager)}] {failure}", this);
        }

        private void OnDisable()
        {
            DeactivateAll();
        }

        private void OnDestroy()
        {
            DeactivateAll();
        }

        private void LateUpdate()
        {
            if (!active)
                return;

            for (int i = 0; i < connections.Length; i++)
            {
                KExactPortalConnection connection = connections[i];
                if (connection != null && connection.IsTransportActive &&
                    !connection.IsFaultLatched)
                {
                    continue;
                }

                string reason = connection == null
                    ? $"Managed connection index {i} was destroyed."
                    : $"Managed connection '{connection.ConnectionKey}' stopped fail-closed: " +
                      (connection.FaultReason ?? "transport became inactive");
                DeactivateAll();
                LatchFault(reason);
                Debug.LogError($"[{nameof(KExactPortalRuntimeManager)}] {reason}", this);
                return;
            }
        }

        private bool TryValidateConnections(out string failure)
        {
            if (connections == null || connections.Length == 0)
            {
                failure = "No K-exact portal connections are assigned.";
                return false;
            }

            var seen = new HashSet<KExactPortalConnection>();
            for (int i = 0; i < connections.Length; i++)
            {
                if (connections[i] == null || !seen.Add(connections[i]))
                {
                    failure = $"Connection array entry {i} is missing or duplicated.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private void LatchFault(string reason)
        {
            active = false;
            faultLatched = true;
            faultReason = reason;
        }

        private static T[] CopyArray<T>(T[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<T>();
            var copy = new T[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }
}
