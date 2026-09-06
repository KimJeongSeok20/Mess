using System.Collections.Generic;
using UnityEngine;

namespace DungeonAdjacentLightingPoC
{
    public static class DungeonAdjacentDoorwayIdentity
    {
        public static string GetStableHierarchyId(Transform subject, Transform root)
        {
            if (subject == null || root == null)
                return string.Empty;
            if (subject != root && !subject.IsChildOf(root))
                return string.Empty;

            var segments = new List<string>();
            Transform current = subject;
            while (current != null && current != root)
            {
                segments.Add($"{current.name}[{current.GetSiblingIndex()}]");
                current = current.parent;
            }
            segments.Reverse();
            return string.Join("/", segments);
        }
    }
}
