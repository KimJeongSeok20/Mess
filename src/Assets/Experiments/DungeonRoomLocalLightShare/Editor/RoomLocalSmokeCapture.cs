using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DungeonRoomLocalLightShare.Editor
{
    public static class RoomLocalSmokeCapture
    {
        public static string CaptureGameView(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                label = "Frame";

            RoomLocalEditorUtil.EnsureFolder(RoomLocalLightShareContract.EvidenceFolder);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string path = RoomLocalLightShareContract.EvidenceFolder + "/" +
                          Sanitize(label) + "_" + stamp + ".png";

            if (!TryCapture(path, out string failure))
                return "FAIL smoke capture: " + failure;

            AssetDatabase.ImportAsset(path);
            return "PASS smoke capture path=" + path +
                   "\nnote=This is a screenshot, not a visual-acceptance claim.";
        }

        private static bool TryCapture(string path, out string failure)
        {
            failure = null;
            Camera camera = Camera.main;
            if (camera == null)
            {
                failure = "Camera.main is missing. Enter Play Mode on the isolated scene first.";
                return false;
            }

            int width = Mathf.Max(64, camera.pixelWidth);
            int height = Mathf.Max(64, camera.pixelHeight);
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = camera.targetTexture;
            camera.targetTexture = rt;
            camera.Render();
            camera.targetTexture = previous;

            RenderTexture active = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply(false, false);
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            return true;
        }

        private static string Sanitize(string label)
        {
            char[] chars = label.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-')
                    chars[i] = '_';
            }

            return new string(chars);
        }
    }
}
