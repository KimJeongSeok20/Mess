using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Demo.Scripts.Runtime.Character.Tests
{
    public static class CameraCollisionSmokeRunner
    {
        private const string CyberGenericPrefabPath = "Assets/FPS/Cyber_Generic.prefab";

        public static void Run()
        {
            bool success;

            try
            {
                success = RunChecks();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                success = false;
            }

            Debug.Log(success
                ? "[SmokeTest] CameraCollision checks passed."
                : "[SmokeTest] CameraCollision checks failed.");

            EditorApplication.Exit(success ? 0 : 1);
        }

        private static bool RunChecks()
        {
            return CheckCyberGenericPrefabWiring()
                   && CheckNoGeometryKeepsEnvelopeNeutral()
                   && CheckFrontWallPitchDownExpandsEnvelope()
                   && CheckFrontWallPitchUpKeepsEnvelopeNeutral()
                   && CheckFrontWallPitchDownThenPitchUpResetsEnvelope()
                   && CheckFarFrontWallKeepsEnvelopeNeutral()
                   && CheckSideWallKeepsEnvelopeNeutral();
        }

        private static bool CheckCyberGenericPrefabWiring()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CyberGenericPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[SmokeTest] Prefab not found at {CyberGenericPrefabPath}.");
                return false;
            }

            GameObject instance = null;

            try
            {
                instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance == null)
                {
                    Debug.LogError("[SmokeTest] Failed to instantiate Cyber_Generic prefab.");
                    return false;
                }

                Type handlerType = ResolveType(
                    "Demo.Scripts.Runtime.Character.CameraCollisionHandler, Assembly-CSharp",
                    "Demo.Scripts.Runtime.Character.CameraCollisionHandler, Assembly-CSharp.Player");

                if (handlerType == null)
                {
                    Debug.LogError("[SmokeTest] Required runtime types were not found.");
                    return false;
                }

                Component handler = instance.GetComponentInChildren(handlerType, true);

                if (handler == null)
                {
                    Debug.LogError("[SmokeTest] CameraCollisionHandler is missing on Cyber_Generic prefab hierarchy.");
                    return false;
                }

                MethodInfo applyEnvelopeMethod = handlerType.GetMethod(
                    "ApplyLookDrivenControllerEnvelope",
                    BindingFlags.Instance | BindingFlags.Public);

                if (applyEnvelopeMethod == null)
                {
                    Debug.LogError("[SmokeTest] ApplyLookDrivenControllerEnvelope method was not found.");
                    return false;
                }

                FieldInfo checkPlayerBodyField = handlerType.GetField("checkPlayerBody", BindingFlags.Instance | BindingFlags.NonPublic);
                if (checkPlayerBodyField == null)
                {
                    Debug.LogError("[SmokeTest] checkPlayerBody field was not found.");
                    return false;
                }

                bool checkPlayerBody = (bool)checkPlayerBodyField.GetValue(handler);
                if (checkPlayerBody)
                {
                    Debug.LogError("[SmokeTest] checkPlayerBody should be false on Cyber_Generic prefab.");
                    return false;
                }

                return true;
            }
            finally
            {
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        private static bool CheckNoGeometryKeepsEnvelopeNeutral()
        {
            Setup(
                out GameObject playerRoot,
                out GameObject cameraObject,
                out Component handler,
                out MethodInfo applyEnvelopeMethod,
                out CharacterController characterController);

            try
            {
                float baseRadius = characterController.radius;
                float baseCenterZ = characterController.center.z;

                InvokeApplyEnvelope(applyEnvelopeMethod, handler, playerRoot.transform, 60f);

                bool ok = Approximately(characterController.radius, baseRadius)
                          && Approximately(characterController.center.z, baseCenterZ);
                if (!ok)
                {
                    Debug.LogError(
                        $"[SmokeTest] No-geometry envelope check failed (radius={characterController.radius:F3}, centerZ={characterController.center.z:F3}).");
                }

                return ok;
            }
            finally
            {
                Teardown(cameraObject, playerRoot, null);
            }
        }

        private static bool CheckFrontWallPitchDownExpandsEnvelope()
        {
            Setup(
                out GameObject playerRoot,
                out GameObject cameraObject,
                out Component handler,
                out MethodInfo applyEnvelopeMethod,
                out CharacterController characterController);

            GameObject wall = null;

            try
            {
                wall = CreateWall(new Vector3(0f, 1.7f, 0.45f), new Vector3(4f, 3f, 0.2f), "FrontWallNear");

                float baseRadius = characterController.radius;
                float baseCenterZ = characterController.center.z;

                InvokeApplyEnvelope(applyEnvelopeMethod, handler, playerRoot.transform, 60f);

                bool expandedRadius = characterController.radius > baseRadius + 0.002f;
                bool movedCenter = characterController.center.z > baseCenterZ + 0.005f;

                bool ok = expandedRadius || movedCenter;
                if (!ok)
                {
                    Debug.LogError(
                        $"[SmokeTest] Front-wall pitch-down expansion failed (radius={characterController.radius:F3}, centerZ={characterController.center.z:F3}).");
                }

                return ok;
            }
            finally
            {
                Teardown(cameraObject, playerRoot, wall);
            }
        }

        private static bool CheckFrontWallPitchUpKeepsEnvelopeNeutral()
        {
            Setup(
                out GameObject playerRoot,
                out GameObject cameraObject,
                out Component handler,
                out MethodInfo applyEnvelopeMethod,
                out CharacterController characterController);

            GameObject wall = null;

            try
            {
                wall = CreateWall(new Vector3(0f, 1.7f, 0.45f), new Vector3(4f, 3f, 0.2f), "FrontWallPitchUp");

                float baseRadius = characterController.radius;
                float baseCenterZ = characterController.center.z;

                InvokeApplyEnvelope(applyEnvelopeMethod, handler, playerRoot.transform, -20f);

                bool ok = Approximately(characterController.radius, baseRadius)
                          && Approximately(characterController.center.z, baseCenterZ);
                if (!ok)
                {
                    Debug.LogError(
                        $"[SmokeTest] Front-wall pitch-up neutral check failed (radius={characterController.radius:F3}, centerZ={characterController.center.z:F3}).");
                }

                return ok;
            }
            finally
            {
                Teardown(cameraObject, playerRoot, wall);
            }
        }

        private static bool CheckFrontWallPitchDownThenPitchUpResetsEnvelope()
        {
            Setup(
                out GameObject playerRoot,
                out GameObject cameraObject,
                out Component handler,
                out MethodInfo applyEnvelopeMethod,
                out CharacterController characterController);

            GameObject wall = null;

            try
            {
                wall = CreateWall(new Vector3(0f, 1.7f, 0.45f), new Vector3(4f, 3f, 0.2f), "FrontWallReset");

                float baseRadius = characterController.radius;
                float baseCenterZ = characterController.center.z;

                InvokeApplyEnvelope(applyEnvelopeMethod, handler, playerRoot.transform, 60f);
                InvokeApplyEnvelope(applyEnvelopeMethod, handler, playerRoot.transform, -15f);

                bool ok = Approximately(characterController.radius, baseRadius)
                          && Approximately(characterController.center.z, baseCenterZ);
                if (!ok)
                {
                    Debug.LogError(
                        $"[SmokeTest] Envelope reset check failed (radius={characterController.radius:F3}, centerZ={characterController.center.z:F3}).");
                }

                return ok;
            }
            finally
            {
                Teardown(cameraObject, playerRoot, wall);
            }
        }

        private static bool CheckFarFrontWallKeepsEnvelopeNeutral()
        {
            Setup(
                out GameObject playerRoot,
                out GameObject cameraObject,
                out Component handler,
                out MethodInfo applyEnvelopeMethod,
                out CharacterController characterController);

            GameObject wall = null;

            try
            {
                wall = CreateWall(new Vector3(0f, 1.7f, 1.2f), new Vector3(4f, 3f, 0.2f), "FrontWallFar");

                float baseRadius = characterController.radius;
                float baseCenterZ = characterController.center.z;

                InvokeApplyEnvelope(applyEnvelopeMethod, handler, playerRoot.transform, 60f);

                bool ok = Approximately(characterController.radius, baseRadius)
                          && Approximately(characterController.center.z, baseCenterZ);
                if (!ok)
                {
                    Debug.LogError(
                        $"[SmokeTest] Far-wall neutral check failed (radius={characterController.radius:F3}, centerZ={characterController.center.z:F3}).");
                }

                return ok;
            }
            finally
            {
                Teardown(cameraObject, playerRoot, wall);
            }
        }

        private static bool CheckSideWallKeepsEnvelopeNeutral()
        {
            Setup(
                out GameObject playerRoot,
                out GameObject cameraObject,
                out Component handler,
                out MethodInfo applyEnvelopeMethod,
                out CharacterController characterController);

            GameObject wall = null;

            try
            {
                wall = CreateWall(new Vector3(0.5f, 1.7f, 0f), new Vector3(0.2f, 3f, 4f), "SideWall");

                float baseRadius = characterController.radius;
                float baseCenterZ = characterController.center.z;

                InvokeApplyEnvelope(applyEnvelopeMethod, handler, playerRoot.transform, 60f);

                bool ok = Approximately(characterController.radius, baseRadius)
                          && Approximately(characterController.center.z, baseCenterZ);
                if (!ok)
                {
                    Debug.LogError(
                        $"[SmokeTest] Side-wall neutral check failed (radius={characterController.radius:F3}, centerZ={characterController.center.z:F3}).");
                }

                return ok;
            }
            finally
            {
                Teardown(cameraObject, playerRoot, wall);
            }
        }

        private static void Setup(
            out GameObject playerRoot,
            out GameObject cameraObject,
            out Component handler,
            out MethodInfo applyEnvelopeMethod,
            out CharacterController characterController)
        {
            playerRoot = new GameObject("SmokePlayerRoot");
            characterController = playerRoot.AddComponent<CharacterController>();
            characterController.center = new Vector3(0f, 1f, 0f);
            characterController.height = 2f;
            characterController.radius = 0.3f;

            cameraObject = new GameObject("SmokeCamera");
            cameraObject.transform.SetParent(playerRoot.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.7f, 0f);

            Type handlerType = ResolveType(
                "Demo.Scripts.Runtime.Character.CameraCollisionHandler, Assembly-CSharp",
                "Demo.Scripts.Runtime.Character.CameraCollisionHandler, Assembly-CSharp.Player");

            if (handlerType == null)
            {
                throw new InvalidOperationException("CameraCollisionHandler type was not found.");
            }

            handler = cameraObject.AddComponent(handlerType);

            MethodInfo setCharacterControllerMethod = handlerType.GetMethod("SetCharacterController", BindingFlags.Instance | BindingFlags.Public);
            applyEnvelopeMethod = handlerType.GetMethod("ApplyLookDrivenControllerEnvelope", BindingFlags.Instance | BindingFlags.Public);

            if (setCharacterControllerMethod == null || applyEnvelopeMethod == null)
            {
                throw new InvalidOperationException("Required CameraCollisionHandler methods were not found.");
            }

            setCharacterControllerMethod.Invoke(handler, new object[] { characterController });

            Physics.SyncTransforms();
        }

        private static void InvokeApplyEnvelope(
            MethodInfo applyEnvelopeMethod,
            Component handler,
            Transform playerTransform,
            float lookPitch)
        {
            object[] args = { playerTransform, lookPitch };
            applyEnvelopeMethod.Invoke(handler, args);
        }

        private static Type ResolveType(string preferredTypeName, string fallbackTypeName)
        {
            return Type.GetType(preferredTypeName) ?? Type.GetType(fallbackTypeName);
        }

        private static GameObject CreateWall(Vector3 position, Vector3 scale, string objectName)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = objectName;
            wall.transform.position = position;
            wall.transform.localScale = scale;

            Physics.SyncTransforms();
            return wall;
        }

        private static bool Approximately(float a, float b)
        {
            return Mathf.Abs(a - b) < 0.001f;
        }

        private static void Teardown(GameObject cameraObject, GameObject playerRoot, GameObject wall)
        {
            if (wall != null)
            {
                UnityEngine.Object.DestroyImmediate(wall);
            }

            if (cameraObject != null)
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }

            if (playerRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(playerRoot);
            }
        }
    }
}
