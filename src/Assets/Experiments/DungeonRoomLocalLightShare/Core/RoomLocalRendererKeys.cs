using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    /// <summary>
    /// Canonical renderer identity: relativePath#occurrence from
    /// GetComponentsInChildren&lt;Renderer&gt;(true). Adapted from DPBB validation contract.
    /// </summary>
    public static class RoomLocalRendererKeys
    {
        public static Dictionary<string, Renderer> BuildKeyMap(Transform roomRoot)
        {
            if (roomRoot == null)
                throw new ArgumentNullException(nameof(roomRoot));

            Renderer[] traversal = roomRoot.GetComponentsInChildren<Renderer>(true);
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var result = new Dictionary<string, Renderer>(StringComparer.Ordinal);
            for (int i = 0; i < traversal.Length; i++)
            {
                Renderer renderer = traversal[i];
                if (renderer == null)
                    throw new InvalidOperationException("Renderer traversal contains null at " + i + ".");

                string barePath = GetBareRelativePath(roomRoot, renderer.transform);
                occurrences.TryGetValue(barePath, out int occurrence);
                occurrences[barePath] = occurrence + 1;
                string key = MakeKey(barePath, occurrence);
                if (result.ContainsKey(key))
                    throw new InvalidOperationException("Duplicate canonical renderer key: " + key);
                result.Add(key, renderer);
            }

            return result;
        }

        public static bool TryBuildKeyMap(
            Transform roomRoot,
            out Dictionary<string, Renderer> keyMap,
            out string failure)
        {
            keyMap = null;
            if (roomRoot == null)
            {
                failure = "Room root is missing.";
                return false;
            }

            try
            {
                keyMap = BuildKeyMap(roomRoot);
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        public static string MakeKey(string relativePath, int occurrence)
        {
            return (relativePath ?? string.Empty) + "#" +
                   occurrence.ToString(CultureInfo.InvariantCulture);
        }

        public static string GetBareRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || (target != root && !target.IsChildOf(root)))
                throw new InvalidOperationException("Renderer transform is outside its room root.");
            if (target == root)
                return string.Empty;

            var parts = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                parts.Push(current.name);
                current = current.parent;
            }

            if (current != root)
                throw new InvalidOperationException("Renderer hierarchy escaped its room root.");
            return string.Join("/", parts);
        }
    }
}
