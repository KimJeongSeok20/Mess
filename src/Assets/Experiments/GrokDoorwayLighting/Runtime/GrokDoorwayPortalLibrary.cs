using System;
using System.Collections.Generic;
using DunGen;
using UnityEngine;

namespace GrokDoorwayLighting
{
    public sealed class GrokDoorwayPortalLibrary
    {
        private readonly List<GrokDoorwayPortalMap> _preauthored;
        private readonly Dictionary<string, Texture> _cache = new Dictionary<string, Texture>();
        private readonly bool _captureMissing;
        private readonly bool _log;

        public GrokDoorwayPortalLibrary(
            IList<GrokDoorwayPortalMap> preauthored,
            bool captureMissing,
            bool log)
        {
            _preauthored = new List<GrokDoorwayPortalMap>();
            if (preauthored != null)
            {
                for (int i = 0; i < preauthored.Count; i++)
                {
                    if (preauthored[i] != null)
                        _preauthored.Add(preauthored[i]);
                }
            }

            _captureMissing = captureMissing;
            _log = log;
        }

        public Texture GetP100(DungeonTileLightmapSwitcher lighting, Doorway doorway)
        {
            if (lighting == null || doorway == null)
                return null;

            string key = MakeKey(lighting, doorway);
            if (_cache.TryGetValue(key, out Texture cached) && cached != null)
                return cached;

            Texture preauthored = FindPreauthored(lighting, doorway);
            if (preauthored != null)
            {
                _cache[key] = preauthored;
                return preauthored;
            }

            if (!_captureMissing)
                return null;

            DungeonTileBakeData bake = lighting.GetBakeData(DungeonTileLightmapSwitcher.PowerLevel.P100);
            Texture2D captured = GrokDoorwayPortalSampler.Capture(
                lighting.transform,
                doorway.transform,
                bake);
            if (captured == null)
            {
                if (_log)
                    Debug.LogWarning(
                        "[GrokDoorway] no P100 portal for " + key + " (existing bake sample failed)",
                        lighting);
                return null;
            }

            _cache[key] = captured;
            if (_log)
                Debug.Log("[GrokDoorway] captured P100 portal " + key, lighting);
            return captured;
        }

        public void DisposeRuntimeCaptures()
        {
            foreach (KeyValuePair<string, Texture> pair in _cache)
            {
                if (pair.Value is Texture2D texture &&
                    (texture.hideFlags & HideFlags.HideAndDontSave) != 0)
                    UnityEngine.Object.Destroy(texture);
            }

            _cache.Clear();
        }

        private Texture FindPreauthored(DungeonTileLightmapSwitcher lighting, Doorway doorway)
        {
            string tileName = GrokDoorwayPortalSampler.NormalizeTileName(lighting.gameObject.name);
            string doorwayPath = NormalizeDoorwayPath(
                GrokDoorwayPortalSampler.RelativePath(lighting.transform, doorway.transform));

            for (int i = 0; i < _preauthored.Count; i++)
            {
                GrokDoorwayPortalMap map = _preauthored[i];
                if (map == null)
                    continue;
                if (map.roomName != tileName)
                    continue;
                if (!string.IsNullOrEmpty(map.doorwayPath) &&
                    NormalizeDoorwayPath(map.doorwayPath) != doorwayPath)
                    continue;

                Texture2D texture = map.GetTexture(DungeonTileLightmapSwitcher.PowerLevel.P100);
                if (texture != null)
                    return texture;
            }

            return null;
        }

        private static string NormalizeDoorwayPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            var parts = path.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                int split = parts[i].LastIndexOf(" (", System.StringComparison.Ordinal);
                if (split > 0 && parts[i].EndsWith(")", System.StringComparison.Ordinal))
                    parts[i] = parts[i].Substring(0, split);
            }

            return string.Join("/", parts);
        }

        private static string MakeKey(DungeonTileLightmapSwitcher lighting, Doorway doorway)
        {
            return GrokDoorwayPortalSampler.NormalizeTileName(lighting.gameObject.name) + "|" +
                   NormalizeDoorwayPath(
                       GrokDoorwayPortalSampler.RelativePath(lighting.transform, doorway.transform)) +
                   "|P100";
        }
    }
}
