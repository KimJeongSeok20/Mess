using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DungeonRoomLocalLightShare
{
    /// <summary>
    /// One incoming cookie-spot. Cookie is required. Geometry is doorway solid-angle,
    /// not GPT's 131° / 57m flood.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomLocalCookieSpot : MonoBehaviour
    {
        [SerializeField] private Transform receiverDoorway;
        [SerializeField] private Transform sourceDoorway;
        [SerializeField] private OutgoingPortalMap sourceOutgoing;
        [SerializeField, Min(0.05f)] private float intensityScale = 1f;
        [SerializeField, Min(0.5f)] private float directRange = RoomLocalDoorwayFrame.DirectRange;
        [SerializeField, Min(0.05f)] private float sourceStandOff = 1f;
        [SerializeField, Range(0.01f, 0.25f)] private float cookieEdgeFeather = 0.12f;
        [SerializeField] private int lightingLayerMask =
            RoomLocalLightShareContract.DungeonRenderingLayerMask;

        private Light beam;
        private bool built;
        private Texture squareCookie;
        private Texture squareCookieSource;
        private float cookieActiveWidth01 = 1f;
        private float cookieActiveHeight01 = 1f;

        public void Configure(
            Transform doorway,
            Transform source,
            OutgoingPortalMap outgoing,
            float scale = 1f,
            float range = RoomLocalDoorwayFrame.DirectRange,
            float standOff = 1f,
            int cookieLightingLayerMask = RoomLocalLightShareContract.DungeonRenderingLayerMask)
        {
            receiverDoorway = doorway;
            sourceDoorway = source;
            sourceOutgoing = outgoing;
            intensityScale = Mathf.Max(0.05f, scale);
            directRange = Mathf.Max(0.5f, range);
            sourceStandOff = Mathf.Max(0.05f, standOff);
            lightingLayerMask = cookieLightingLayerMask != 0
                ? cookieLightingLayerMask
                : RoomLocalLightShareContract.DungeonRenderingLayerMask;
            Rebuild();
        }

        public bool TryApply(
            float sourcePower01,
            float aperture01,
            bool connectionEnabled,
            out string failure)
        {
            float beamWeight = connectionEnabled
                ? RoomLocalLightShareMath.ComposeTransferWeight(sourcePower01, aperture01)
                : 0f;
            if (beamWeight <= 0.00001f)
            {
                DisableAll();
                failure = null;
                return true;
            }

            if (!built)
                Rebuild();
            if (beam == null)
            {
                failure = "Cookie-spot light was not created.";
                return false;
            }

            if (sourceOutgoing == null)
            {
                failure = "Outgoing portal map is missing.";
                DisableAll();
                return false;
            }

            if (!sourceOutgoing.TryGetRuntimeCookie(
                    sourcePower01,
                    out Texture cookie,
                    out float peakRadiance,
                    out failure))
            {
                DisableAll();
                return false;
            }

            Texture spotCookie = EnsureSquareCookie(cookie);
            // The cookie already contains normalized linear RGB. Tinting the Light with the
            // average chromaticity multiplies the color a second time. White light times the
            // captured normalization peak reconstructs the authored portal RGB once.
            ApplyLight(beam, spotCookie, peakRadiance * intensityScale * beamWeight);
            failure = null;
            return true;
        }

        public void SetRuntimeTuning(float scale, float range)
        {
            intensityScale = Mathf.Max(0.05f, scale);
            directRange = Mathf.Max(0.5f, range);
            if (beam != null)
            {
                beam.range = directRange;
                UpdateShadowNearPlane();
            }
        }

        public void SetShadowRenderingLayers(int shadowRenderingLayerMask)
        {
            if (beam == null)
                return;
            UniversalAdditionalLightData data = beam.GetUniversalAdditionalLightData();
            data.customShadowLayers = true;
            data.shadowRenderingLayers = (uint)shadowRenderingLayerMask;
        }

        public bool TrySampleRadiance(Vector3 worldPosition, float sourcePower01, float aperture01,
            out Color radiance, out Vector3 portalEntry, out Vector3 directionToLight)
        {
            radiance = Color.black;
            portalEntry = directionToLight = Vector3.zero;
            if (!TryApply(sourcePower01, aperture01, true, out _) || beam == null || !beam.enabled ||
                !(squareCookie is Texture2D cookie))
                return false;

            Vector3 local = beam.transform.InverseTransformPoint(worldPosition);
            // A spot's virtual source is behind the doorway. It must never contribute SH
            // to that source room, even though the Unity spot cone extends into it.
            if (local.z <= sourceStandOff || float.IsNaN(local.z) || float.IsInfinity(local.z))
                return false;
            Vector3 fromLight = worldPosition - beam.transform.position;
            float distanceSqr = fromLight.sqrMagnitude;
            float rangeSqr = directRange * directRange;
            if (distanceSqr >= rangeSqr || distanceSqr <= 0.00001f)
                return false;
            portalEntry = beam.transform.position + fromLight * (sourceStandOff / local.z);
            Vector2 socket = RoomLocalDoorwayFrame.OverlappingSocketSize(
                ResolveSocketSize(receiverDoorway), ResolveSocketSize(sourceDoorway));
            Vector3 atDoor = receiverDoorway.InverseTransformPoint(portalEntry);
            if (Mathf.Abs(atDoor.x) > socket.x * 0.5f || atDoor.y < 0f || atDoor.y > socket.y)
                return false;

            float halfCone = Mathf.Tan(beam.spotAngle * 0.5f * Mathf.Deg2Rad) * local.z;
            float u = 0.5f + local.x / (2f * halfCone);
            float v = 0.5f + local.y / (2f * halfCone);
            if (u < 0f || u > 1f || v < 0f || v > 1f)
                return false;

            float distance = Mathf.Sqrt(distanceSqr);
            float cosine = local.z / distance;
            float outer = Mathf.Cos(beam.spotAngle * 0.5f * Mathf.Deg2Rad);
            float inner = Mathf.Cos(beam.innerSpotAngle * 0.5f * Mathf.Deg2Rad);
            float angular = Mathf.Clamp01((cosine - outer) / Mathf.Max(0.001f, inner - outer));
            float distanceFactor = distanceSqr / rangeSqr;
            float smoothRange = Mathf.Clamp01(1f - distanceFactor * distanceFactor);
            // Same distance and cone attenuation as URP's RealtimeLights.hlsl. Reuse the
            // existing readable cookie, including its RGB normalization and edge feather.
            radiance = cookie.GetPixelBilinear(u, v) *
                (beam.intensity * angular * angular * smoothRange * smoothRange / distanceSqr);
            directionToLight = -fromLight / distance;
            return RoomLocalLightShareMath.Luminance(radiance) > 0.000001f;
        }

        public void DisableAll()
        {
            if (beam != null)
                beam.enabled = false;
        }

        private void OnDisable()
        {
            DisableAll();
        }

        private void OnDestroy()
        {
            ReleaseSquareCookie();
        }

        private void Rebuild()
        {
            ReleaseSquareCookie();
            DestroyChild("Beam");
            DestroyChild("LeafFill");
            if (receiverDoorway == null)
            {
                built = false;
                return;
            }

            Vector2 socket = RoomLocalDoorwayFrame.OverlappingSocketSize(
                ResolveSocketSize(receiverDoorway),
                ResolveSocketSize(sourceDoorway));

            float largestSocketDimension = Mathf.Max(socket.x, socket.y);
            if (largestSocketDimension > 0.001f)
            {
                cookieActiveWidth01 = Mathf.Clamp01(socket.x / largestSocketDimension);
                cookieActiveHeight01 = Mathf.Clamp01(socket.y / largestSocketDimension);
            }

            RoomLocalDoorwayFrame.Frame frame = RoomLocalDoorwayFrame.FromDoorway(receiverDoorway, socket);
            float spotAngle = RoomLocalDoorwayFrame.FittedSpotAngle(frame, sourceStandOff);
            Vector3 position = RoomLocalDoorwayFrame.SpotPosition(frame, sourceStandOff);
            Quaternion rotation = RoomLocalDoorwayFrame.SpotRotation(frame);

            beam = CreateSpot(
                "Beam",
                position,
                rotation,
                spotAngle,
                directRange,
                LightShadows.Soft,
                RoomLocalLightShareContract.DoorwayShadowRenderingLayerMask);
            UpdateShadowNearPlane();
            built = true;
        }

        private void UpdateShadowNearPlane()
        {
            if (beam == null)
                return;
            // The virtual source sits behind the portal. Ignore occluders in that source
            // region, while keeping the doorway and every leaf in the receiving room.
            beam.shadowNearPlane = Mathf.Clamp(sourceStandOff - 0.05f,
                0.01f, Mathf.Max(0.01f, directRange - 0.01f));
        }

        private static Vector2 ResolveSocketSize(Transform doorwayTransform)
        {
            var doorway = doorwayTransform != null
                ? doorwayTransform.GetComponent<DunGen.Doorway>()
                : null;
            return doorway != null && doorway.Socket != null
                ? doorway.Socket.Size
                : new Vector2(
                    RoomLocalDoorwayFrame.DefaultSocketWidth,
                    RoomLocalDoorwayFrame.DefaultSocketHeight);
        }

        private Light CreateSpot(
            string childName,
            Vector3 position,
            Quaternion rotation,
            float spotAngle,
            float range,
            LightShadows shadows,
            int shadowRenderingLayerMask)
        {
            var child = new GameObject(childName);
            child.transform.SetParent(transform, false);
            child.transform.SetPositionAndRotation(position, rotation);
            Light light = child.AddComponent<Light>();
            light.type = LightType.Spot;
#if UNITY_EDITOR
            light.lightmapBakeType = LightmapBakeType.Realtime;
#endif
            light.spotAngle = spotAngle;
            light.innerSpotAngle = Mathf.Max(1f, spotAngle * 0.65f);
            light.range = range;
            light.shadows = shadows;
            light.shadowStrength = 1f;
            light.shadowBias = 0.05f;
            light.shadowNormalBias = 0.25f;
            light.cullingMask = RoomLocalLightShareContract.DungeonCullingMask;
            light.renderingLayerMask = lightingLayerMask;
            light.enabled = false;
            UniversalAdditionalLightData data = light.GetUniversalAdditionalLightData();
            data.renderingLayers = (uint)lightingLayerMask;
            data.customShadowLayers = true;
            data.shadowRenderingLayers =
                (uint)shadowRenderingLayerMask;
            return light;
        }

        private Texture EnsureSquareCookie(Texture cookie)
        {
            if (cookie == null)
                return null;
            if (squareCookie != null && squareCookieSource == cookie)
                return squareCookie;

            int srcW = cookie.width;
            int srcH = cookie.height;
            int size = Mathf.Max(srcW, srcH);
            int x0 = (size - srcW) / 2;
            int y0 = (size - srcH) / 2;
            RenderTexture sourceRt = RenderTexture.GetTemporary(
                srcW, srcH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture squareRt = RenderTexture.GetTemporary(
                size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(cookie, sourceRt);
            Graphics.Blit(Texture2D.blackTexture, squareRt);
            Graphics.CopyTexture(sourceRt, 0, 0, 0, 0, srcW, srcH, squareRt, 0, 0, x0, y0);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = squareRt;
            var square = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = cookie.name + "_Square",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            square.ReadPixels(new Rect(0f, 0f, size, size), 0, 0);
            Color[] pixels = square.GetPixels();
            float denominator = Mathf.Max(1f, size - 1f);
            for (int y = 0; y < size; y++)
            {
                float v = y / denominator;
                float vertical = RoomLocalLightShareMath.ComputeApertureFeather(
                    v,
                    cookieActiveHeight01,
                    cookieEdgeFeather);
                for (int x = 0; x < size; x++)
                {
                    float u = x / denominator;
                    float horizontal = RoomLocalLightShareMath.ComputeApertureFeather(
                        u,
                        cookieActiveWidth01,
                        cookieEdgeFeather);
                    pixels[y * size + x] *= horizontal * vertical;
                }
            }
            square.SetPixels(pixels);
            square.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(sourceRt);
            RenderTexture.ReleaseTemporary(squareRt);

            ReleaseSquareCookie();

            squareCookie = square;
            squareCookieSource = cookie;
            return squareCookie;
        }

        private void ReleaseSquareCookie()
        {
            if (squareCookie != null)
            {
                if (Application.isPlaying)
                    Destroy(squareCookie);
                else
                    DestroyImmediate(squareCookie);
            }

            squareCookie = null;
            squareCookieSource = null;
        }

        private static void ApplyLight(Light light, Texture cookie, float intensity)
        {
            if (cookie == null)
            {
                light.enabled = false;
                light.cookie = null;
                return;
            }

            light.cookie = cookie;
            light.color = Color.white;
            light.intensity = Mathf.Max(0f, intensity);
            light.enabled = intensity > 0.0001f;
        }

        private void DestroyChild(string childName)
        {
            Transform child = transform.Find(childName);
            if (child == null)
                return;
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }
}
