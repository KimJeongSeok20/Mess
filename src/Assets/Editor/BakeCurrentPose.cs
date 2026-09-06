using UnityEngine;
using UnityEditor;

public class BakeCurrentPose : EditorWindow
{
    GameObject root;
    string clipName = "TPose_Generic";

    [MenuItem("Tools/Pose/Bake Current Pose To Clip")]
    public static void Open() => GetWindow<BakeCurrentPose>("Bake Pose");

    void OnGUI()
    {
        root = (GameObject)EditorGUILayout.ObjectField("Root(Animator)", root, typeof(GameObject), true);
        clipName = EditorGUILayout.TextField("Clip Name", clipName);

        if (GUILayout.Button("Bake 0-frame Clip"))
        {
            if (!root) { Debug.LogError("Select a root with Animator."); return; }
            var anim = root.GetComponent<Animator>();
            if (!anim) { Debug.LogError("No Animator on root."); return; }

            var clip = new AnimationClip { frameRate = 30f };

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root.transform) continue;
                string path = AnimationUtility.CalculateTransformPath(t, root.transform);

                // 로컬 회전(필수)
                var r = t.localRotation;
                var rx = new AnimationCurve(new Keyframe(0f, r.x));
                var ry = new AnimationCurve(new Keyframe(0f, r.y));
                var rz = new AnimationCurve(new Keyframe(0f, r.z));
                var rw = new AnimationCurve(new Keyframe(0f, r.w));
                clip.SetCurve(path, typeof(Transform), "localRotation.x", rx);
                clip.SetCurve(path, typeof(Transform), "localRotation.y", ry);
                clip.SetCurve(path, typeof(Transform), "localRotation.z", rz);
                clip.SetCurve(path, typeof(Transform), "localRotation.w", rw);

                // 필요하면 위치/스케일도 고정하려면 주석 해제
                // var p = t.localPosition;
                // clip.SetCurve(path, typeof(Transform), "m_LocalPosition.x", new AnimationCurve(new Keyframe(0, p.x)));
                // clip.SetCurve(path, typeof(Transform), "m_LocalPosition.y", new AnimationCurve(new Keyframe(0, p.y)));
                // clip.SetCurve(path, typeof(Transform), "m_LocalPosition.z", new AnimationCurve(new Keyframe(0, p.z)));
                // var s = t.localScale;
                // clip.SetCurve(path, typeof(Transform), "m_LocalScale.x", new AnimationCurve(new Keyframe(0, s.x)));
                // clip.SetCurve(path, typeof(Transform), "m_LocalScale.y", new AnimationCurve(new Keyframe(0, s.y)));
                // clip.SetCurve(path, typeof(Transform), "m_LocalScale.z", new AnimationCurve(new Keyframe(0, s.z)));
            }

            // 루프 끄기
            var so = new SerializedObject(clip);
            so.FindProperty("m_AnimationClipSettings.m_LoopTime").boolValue = false;
            so.ApplyModifiedProperties();

            var save = EditorUtility.SaveFilePanelInProject("Save TPose Clip", clipName, "anim", "");
            if (!string.IsNullOrEmpty(save))
            {
                AssetDatabase.CreateAsset(clip, save);
                AssetDatabase.SaveAssets();
                Debug.Log("Saved: " + save);
            }
        }
    }
}
