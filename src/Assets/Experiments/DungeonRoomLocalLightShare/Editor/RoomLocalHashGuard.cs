using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DungeonRoomLocalLightShare.Editor
{
    public static class RoomLocalHashGuard
    {
        private static readonly string[] GuardedPaths =
        {
            RoomLocalLightShareContract.StartPrefabPath,
            RoomLocalLightShareContract.AdministrativePrefabPath,
            RoomLocalLightShareContract.StartP0BakePath,
            RoomLocalLightShareContract.StartP100BakePath,
            RoomLocalLightShareContract.AdministrativeP0BakePath,
            RoomLocalLightShareContract.AdministrativeP100BakePath,
            RoomLocalLightShareContract.DoorPrefabPath,
            RoomLocalLightShareContract.LightmapSwitcherPath
        };

        public static string CaptureOrVerify()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < GuardedPaths.Length; i++)
            {
                string path = GuardedPaths[i];
                if (!File.Exists(path))
                    return "FAIL: guarded production input is missing: " + path;
                builder.Append(path);
                builder.Append('=');
                builder.Append(HashFile(path));
                builder.AppendLine();
            }

            string current = builder.ToString();
            string hashPath = RoomLocalLightShareContract.ProductionHashPath;
            if (!File.Exists(hashPath))
            {
                RoomLocalEditorUtil.EnsureFolder(RoomLocalLightShareContract.DataFolder);
                File.WriteAllText(hashPath, current);
                AssetDatabase.ImportAsset(hashPath);
                return "PASS captured production input hashes\n" + current;
            }

            string previous = File.ReadAllText(hashPath).Replace("\r\n", "\n");
            if (!string.Equals(previous, current.Replace("\r\n", "\n"), StringComparison.Ordinal))
            {
                return "FAIL production input hash drifted.\nexpected:\n" + previous +
                       "\nactual:\n" + current;
            }

            return "PASS production input hashes match\n" + current;
        }

        public static string HashFile(string assetPath)
        {
            using (var sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(assetPath))
            {
                byte[] hash = sha.ComputeHash(stream);
                var text = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    text.Append(hash[i].ToString("X2"));
                return text.ToString();
            }
        }
    }
}
