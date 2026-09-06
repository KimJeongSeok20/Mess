using System;

namespace DungeonRoomLocalLightShare
{
    /// <summary>
    /// Live DunGen tiles may be rotation wrappers. Catalog paths were captured after
    /// V2 rotation selection, so matching has to ignore Canonical/R000 prefixes.
    /// </summary>
    public static class RoomLocalEndpointKeys
    {
        public static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            const string clone = "(Clone)";
            string trimmed = name.Trim();
            if (trimmed.EndsWith(clone, StringComparison.Ordinal))
                trimmed = trimmed.Substring(0, trimmed.Length - clone.Length).TrimEnd();
            return trimmed;
        }

        public static string StripRotationSuffix(string name)
        {
            string trimmed = StripCloneSuffix(name);
            if (string.IsNullOrEmpty(trimmed))
                return string.Empty;

            int index = trimmed.LastIndexOf("_R", StringComparison.Ordinal);
            if (index <= 0 || index + 2 >= trimmed.Length)
                return trimmed;

            string tail = trimmed.Substring(index + 2);
            if (tail.Length == 0)
                return trimmed;
            for (int i = 0; i < tail.Length; i++)
            {
                if (!char.IsDigit(tail[i]))
                    return trimmed;
            }

            return trimmed.Substring(0, index);
        }

        public static bool RoomIdsMatch(string liveName, string catalogRoomId)
        {
            if (string.IsNullOrEmpty(catalogRoomId))
                return false;
            return string.Equals(
                StripRotationSuffix(liveName),
                StripRotationSuffix(catalogRoomId),
                StringComparison.Ordinal);
        }

        public static string NormalizeDoorwayPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string normalized = path.Replace('\\', '/').Trim('/');
            while (true)
            {
                int slash = normalized.IndexOf('/');
                if (slash <= 0)
                    return normalized;

                string head = normalized.Substring(0, slash);
                if (!IsIgnorablePathSegment(head))
                    return normalized;

                normalized = normalized.Substring(slash + 1);
            }
        }

        public static bool PathsMatch(string livePath, string authoredPath)
        {
            string live = NormalizeDoorwayPath(livePath);
            string authored = NormalizeDoorwayPath(authoredPath);
            if (live.Length == 0 || authored.Length == 0)
                return false;
            if (string.Equals(live, authored, StringComparison.Ordinal))
                return true;
            if (live.EndsWith("/" + authored, StringComparison.Ordinal))
                return true;
            return authored.EndsWith("/" + live, StringComparison.Ordinal);
        }

        private static bool IsIgnorablePathSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment))
                return false;
            if (string.Equals(segment, "Canonical", StringComparison.OrdinalIgnoreCase))
                return true;
            if (segment.Length < 2 || segment[0] != 'R')
                return false;
            for (int i = 1; i < segment.Length; i++)
            {
                if (!char.IsDigit(segment[i]))
                    return false;
            }

            return true;
        }
    }
}
