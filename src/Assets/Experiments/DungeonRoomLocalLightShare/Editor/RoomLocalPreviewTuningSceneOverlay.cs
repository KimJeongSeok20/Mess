using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonRoomLocalLightShare.Editor
{
    [InitializeOnLoad]
    public static class RoomLocalPreviewTuningSceneOverlay
    {
        private const float MinIntensityMultiplier = 0.25f;
        private const float MaxIntensityMultiplier = 2f;
        private const float MinDirectRange = 2.5f;
        private const float MaxDirectRange = 5f;

        static RoomLocalPreviewTuningSceneOverlay()
        {
            SceneView.duringSceneGui += Draw;
        }

        private static void Draw(SceneView sceneView)
        {
            if (!EditorApplication.isPlaying)
                return;

            // This is an experiment-only tuning overlay. Never expose it in the
            // production StartMap where it previously controlled an arbitrary first connection.
            if (SceneManager.GetActiveScene().name == "StartMap")
                return;

            RoomLocalConnection connection =
                Object.FindFirstObjectByType<RoomLocalConnection>();
            if (connection == null)
                return;

            Handles.BeginGUI();
            GUILayout.BeginArea(
                new Rect(16f, 42f, 380f, 92f),
                "DIRECT LIGHT TUNING (PLAY MODE)",
                GUI.skin.window);

            EditorGUI.BeginChangeCheck();
            float intensity = EditorGUILayout.Slider(
                "Intensity multiplier",
                connection.DirectIntensityMultiplier,
                MinIntensityMultiplier,
                MaxIntensityMultiplier);
            float range = EditorGUILayout.Slider(
                "Range (m)",
                connection.DirectRange,
                MinDirectRange,
                MaxDirectRange);
            if (EditorGUI.EndChangeCheck())
            {
                connection.SetRuntimeDirectTuning(intensity, range);
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }
    }
}
