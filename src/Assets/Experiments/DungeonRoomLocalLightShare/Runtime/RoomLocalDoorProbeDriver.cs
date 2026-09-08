using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonRoomLocalLightShare
{
    public enum DoorRealtimeMode
    {
        ProbeOnly = 0,
        CookieReceive = 1,
        PerFaceDirect = 2
    }

    /// <summary>
    /// Samples room SH through DungeonTileProbeRegistry in generated dungeons, preserving
    /// its visibility filtering. Isolated previews without a registry use the baked entries.
    /// PositiveZ/NegativeZ keep the closed-pose room only while the sample sits near the
    /// doorway. Once a face has swung clearly into a room, that location wins. Edge pieces
    /// always follow the current normal's location.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class RoomLocalDoorProbeDriver : MonoBehaviour
    {
        private const int BlendProbeCount = 4;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly Vector3[] EdgeDirections =
        {
            Vector3.right,
            Vector3.left,
            Vector3.up,
            Vector3.down
        };
        private static readonly string[] EdgeDirectionNames =
        {
            "PositiveX",
            "NegativeX",
            "PositiveY",
            "NegativeY"
        };

        [SerializeField] private Transform doorLeaf;
        [SerializeField] private Transform startRoot;
        [SerializeField] private Transform administrativeRoot;
        [SerializeField] private Transform startDoorway;
        [SerializeField] private Transform administrativeDoorway;
        [SerializeField] private DungeonTileLightmapSwitcher startLighting;
        [SerializeField] private DungeonTileLightmapSwitcher administrativeLighting;
        [SerializeField] private OutgoingPortalMap startOutgoing;
        [SerializeField] private OutgoingPortalMap administrativeOutgoing;
        [SerializeField, Min(0.01f)] private float sideSampleOffset = 0.35f;
        [SerializeField, Min(0.01f)] private float planeBlendDistance = 0.08f;
        [SerializeField, Min(0.05f)] private float locationConfidenceDistance = 0.65f;
        [SerializeField, Min(0f)] private float perFaceDirectScale = 0.45f;
        [SerializeField] private bool enablePerFaceDirectResponse;
        [SerializeField] private bool receiveCookieOnDoor;
        [SerializeField] private DoorRealtimeMode realtimeMode = DoorRealtimeMode.CookieReceive;

        private readonly List<RendererBinding> bindings = new List<RendererBinding>();
        private readonly List<Renderer> runtimeEdgeSources = new List<Renderer>();
        private readonly List<GameObject> runtimeEdgeObjects = new List<GameObject>();
        private readonly List<Mesh> runtimeEdgeMeshes = new List<Mesh>();
        private readonly SphericalHarmonicsL2[] singleProbe = new SphericalHarmonicsL2[1];
        private readonly Vector4[] singleOcclusion = new Vector4[1];
        private readonly float[] bestDistances = new float[BlendProbeCount];
        private readonly int[] bestIndices = new int[BlendProbeCount];
        private MaterialPropertyBlock workingBlock;
        private Material runtimeOpaqueMaterial;
        private RoomLocalConnection connection;
        private DunGen.Tile startTile;
        private DunGen.Tile administrativeTile;
        private Renderer shadowCasterRenderer;
        private bool shadowCasterStateCaptured;
        private bool originalShadowCasterEnabled;
        private bool originalShadowCasterReceiveShadows;
        private ShadowCastingMode originalShadowCastingMode;
        private uint originalShadowCasterRenderingLayerMask;
        private bool initialized;
        private bool applied;
        private bool restorationPending;
        private bool faultLatched;
        private string faultReason = string.Empty;
        private float lastStartFacingLuminance;
        private float lastAdministrativeFacingLuminance;
        private float lastEdgeLuminance;
        private bool lastUsedFaceNormalSampling;
        private bool lastUsedClosedFaceOwnership;

        public bool IsApplied => applied;
        public bool IsFaultLatched => faultLatched;
        public string FaultReason => faultReason;
        public int BoundRendererCount => bindings.Count;
        public float LastStartFacingLuminance => lastStartFacingLuminance;
        public float LastAdministrativeFacingLuminance => lastAdministrativeFacingLuminance;
        public float LastEdgeLuminance => lastEdgeLuminance;
        public bool LastUsedFaceNormalSampling => lastUsedFaceNormalSampling;
        public bool LastUsedClosedFaceOwnership => lastUsedClosedFaceOwnership;
        public DoorRealtimeMode RealtimeMode => realtimeMode;
        public bool ReceivesCookie => receiveCookieOnDoor;
        public bool UsesPerFaceDirect => enablePerFaceDirectResponse;

        public void Configure(
            Transform leaf,
            Transform start,
            Transform administrative,
            Transform startDoor,
            Transform administrativeDoor,
            DungeonTileLightmapSwitcher startSwitcher,
            DungeonTileLightmapSwitcher administrativeSwitcher,
            OutgoingPortalMap startMap,
            OutgoingPortalMap administrativeMap)
        {
            doorLeaf = leaf;
            startRoot = start;
            administrativeRoot = administrative;
            startDoorway = startDoor;
            administrativeDoorway = administrativeDoor;
            startLighting = startSwitcher;
            administrativeLighting = administrativeSwitcher;
            startOutgoing = startMap;
            administrativeOutgoing = administrativeMap;
            connection = GetComponent<RoomLocalConnection>();
            CacheRoomTiles();
            initialized = false;
            faultLatched = false;
            faultReason = string.Empty;

            ApplyRealtimeMode(realtimeMode, false);
            if (Application.isPlaying && !TryInitialize(out string failure))
                LatchFault(failure);
        }

        public void SetRealtimeMode(DoorRealtimeMode mode)
        {
            ApplyRealtimeMode(mode, true);
        }

        private void ApplyRealtimeMode(DoorRealtimeMode mode, bool applyNow)
        {
            realtimeMode = mode;
            receiveCookieOnDoor = mode == DoorRealtimeMode.CookieReceive;
            enablePerFaceDirectResponse = mode == DoorRealtimeMode.PerFaceDirect;
            if (!applyNow || !initialized || faultLatched)
                return;
            ApplyVisibleSurfacePolicy();
            TryApplyNow(out _);
        }

        private void Awake()
        {
            if (!TryInitialize(out string failure))
                LatchFault(failure);
        }

        private void OnEnable()
        {
            SubscribePowerEvents();
            if (Application.isPlaying && !faultLatched && !TryInitialize(out string failure))
                LatchFault(failure);
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || faultLatched)
                return;
            if (!initialized && !TryInitialize(out string initializationFailure))
            {
                LatchFault(initializationFailure);
                return;
            }

            if (!TryApplyNow(out string failure))
                LatchFault(failure);
        }

        private void OnDisable()
        {
            UnsubscribePowerEvents();
            RestoreOriginalState();
        }

        private void OnDestroy()
        {
            UnsubscribePowerEvents();
            RestoreOriginalState();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!initialized)
                return;

            Vector3 intoStart = startDoorway != null ? -startDoorway.forward : Vector3.forward;
            Vector3 planePoint = startDoorway != null ? startDoorway.position : Vector3.zero;
            for (int i = 0; i < bindings.Count; i++)
            {
                RendererBinding binding = bindings[i];
                if (binding.Renderer == null)
                    continue;

                bool edge = binding.Group == DungeonDoorProbeRendererGroup.Group.Edge;
                Vector3 origin = binding.Renderer.bounds.center;
                Vector3 direction = ResolveWorldNormal(binding);
                Vector3 sample = origin + direction * sideSampleOffset;
                float side = RoomLocalLightShareMath.DoorwayPlaneSide(sample, planePoint, intoStart);
                float locationWeight = RoomLocalLightShareMath.DoorwayPlaneRoomWeight(
                    side,
                    planeBlendDistance);
                float startWeight = edge
                    ? locationWeight
                    : RoomLocalLightShareMath.BlendClosedAndLocationRoomWeight(
                        binding.OwnsStartRoom,
                        locationWeight,
                        RoomLocalLightShareMath.LocationConfidence(
                            side,
                            locationConfidenceDistance));
                Gizmos.color = Color.Lerp(
                    new Color(1f, 0.75f, 0.15f, 1f),
                    new Color(0.2f, 0.85f, 1f, 1f),
                    startWeight);
                Gizmos.DrawRay(origin, direction * sideSampleOffset);
                Gizmos.DrawSphere(sample, 0.04f);
            }
        }
#endif

        public bool TryApplyNow(out string failure)
        {
            if (!initialized && !TryInitialize(out failure))
                return false;

            DungeonTileBakeData startBake = CurrentBake(startLighting);
            DungeonTileBakeData administrativeBake = CurrentBake(administrativeLighting);
            if (DungeonTileProbeRegistry.Active == null &&
                (!HasProbeData(startBake) || !HasProbeData(administrativeBake)))
            {
                failure = "Door probe driver requires P0/P100 baked probe entries for both rooms.";
                return false;
            }

            lastUsedFaceNormalSampling = true;
            lastUsedClosedFaceOwnership = true;
            lastStartFacingLuminance = 0f;
            lastAdministrativeFacingLuminance = 0f;
            lastEdgeLuminance = 0f;
            int startFacingCount = 0;
            int administrativeFacingCount = 0;
            int edgeCount = 0;

            for (int i = 0; i < bindings.Count; i++)
            {
                RendererBinding binding = bindings[i];
                if (binding.Renderer == null)
                    continue;

                Vector3 center = binding.Renderer.bounds.center;
                bool edge = binding.Group == DungeonDoorProbeRendererGroup.Group.Edge;
                if (!TrySampleAlongCurrentNormal(
                        binding,
                        center,
                        !edge,
                        startRoot,
                        startBake,
                        administrativeRoot,
                        administrativeBake,
                        out SphericalHarmonicsL2 targetProbe,
                        out Vector4 targetOcclusion,
                        out float startWeight,
                        out failure))
                    return false;

                Color directResponse = enablePerFaceDirectResponse
                    ? EvaluatePerFaceDirectResponse(ResolveWorldNormal(binding))
                    : Color.black;
                if (receiveCookieOnDoor)
                    binding.Renderer.renderingLayerMask = ResolveCookieReceiveLayer(binding, center);
                float luminance = L0Luminance(targetProbe);
                if (edge)
                {
                    lastEdgeLuminance += luminance;
                    edgeCount++;
                }
                else if (startWeight >= 0.5f)
                {
                    lastStartFacingLuminance += luminance;
                    startFacingCount++;
                }
                else
                {
                    lastAdministrativeFacingLuminance += luminance;
                    administrativeFacingCount++;
                }

                ApplyProbe(binding, targetProbe, targetOcclusion, directResponse);
            }

            if (startFacingCount > 0)
                lastStartFacingLuminance /= startFacingCount;
            if (administrativeFacingCount > 0)
                lastAdministrativeFacingLuminance /= administrativeFacingCount;
            if (edgeCount > 0)
                lastEdgeLuminance /= edgeCount;

            applied = true;
            failure = null;
            return true;
        }

        public string BuildDiagnostics()
        {
            string startPower = startLighting != null
                ? startLighting.CurrentPowerLevel.ToString()
                : "null";
            string administrativePower = administrativeLighting != null
                ? administrativeLighting.CurrentPowerLevel.ToString()
                : "null";
            return
                $"applied={applied} fault={faultLatched} bindings={bindings.Count} " +
                $"start={startPower} admin={administrativePower} " +
                $"startFacingL0={lastStartFacingLuminance:0.0000} " +
                $"adminFacingL0={lastAdministrativeFacingLuminance:0.0000} " +
                $"edgeL0={lastEdgeLuminance:0.0000} " +
                $"faceNormalSampling={lastUsedFaceNormalSampling} " +
                $"closedFaceOwnership={lastUsedClosedFaceOwnership} " +
                $"realtimeMode={realtimeMode} " +
                $"cookieReceive={receiveCookieOnDoor} " +
                $"perFaceDirect={enablePerFaceDirectResponse} " +
                $"shadowCaster={(shadowCasterRenderer != null && shadowCasterRenderer.enabled)}";
        }

        private bool TryInitialize(out string failure)
        {
            if (initialized)
            {
                failure = null;
                return true;
            }

            if (doorLeaf == null || startRoot == null || administrativeRoot == null ||
                startDoorway == null || administrativeDoorway == null ||
                startLighting == null || administrativeLighting == null ||
                startOutgoing == null || administrativeOutgoing == null)
            {
                failure = "Door probe driver is missing its door, room, or lighting references.";
                return false;
            }

            CacheRoomTiles();
            bindings.Clear();
            var uniqueRenderers = new HashSet<Renderer>();
            DungeonDoorProbeRendererGroup[] groups =
                doorLeaf.GetComponentsInChildren<DungeonDoorProbeRendererGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                DungeonDoorProbeRendererGroup group = groups[i];
                if (group == null)
                    continue;
                Renderer renderer = group.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled || !uniqueRenderers.Add(renderer))
                    continue;

                if (group.ProbeGroup == DungeonDoorProbeRendererGroup.Group.Edge)
                {
                    if (!TryCreateDirectionalEdgeBindings(renderer, group.transform, out failure))
                    {
                        DestroyRuntimeEdgeBindings();
                        return false;
                    }
                    continue;
                }

                Vector3 localNormal = group.ProbeGroup ==
                                      DungeonDoorProbeRendererGroup.Group.NegativeZ
                    ? Vector3.back
                    : Vector3.forward;
                Vector3 closedWorldNormal = ResolveClosedWorldNormal(group.transform, localNormal);
                bindings.Add(new RendererBinding(
                    renderer,
                    group.ProbeGroup,
                    group.transform,
                    localNormal,
                    closedWorldNormal,
                    OwnsStartRoom(closedWorldNormal)));
            }

            if (bindings.Count == 0)
            {
                failure = "Door probe driver found no enabled split-door renderers.";
                return false;
            }

            workingBlock = new MaterialPropertyBlock();
            for (int i = 0; i < bindings.Count; i++)
                bindings[i].CaptureOriginalState();
            restorationPending = true;

            if (!ApplyVisibleSurfacePolicy(out failure))
                return false;

            if (!TryConfigureDoorShadowCaster(out failure))
                return false;

            initialized = true;
            failure = null;
            return true;
        }

        private bool TryConfigureDoorShadowCaster(out string failure)
        {
            shadowCasterRenderer = doorLeaf.GetComponent<Renderer>();
            if (shadowCasterRenderer == null)
            {
                failure = "Door probe driver found no full-mesh source renderer for shadows.";
                return false;
            }

            if (!shadowCasterStateCaptured)
            {
                shadowCasterStateCaptured = true;
                originalShadowCasterEnabled = shadowCasterRenderer.enabled;
                originalShadowCasterReceiveShadows = shadowCasterRenderer.receiveShadows;
                originalShadowCastingMode = shadowCasterRenderer.shadowCastingMode;
                originalShadowCasterRenderingLayerMask =
                    shadowCasterRenderer.renderingLayerMask;
            }

            // The visible door is rendered by three probe-split children. Reusing the disabled
            // full source mesh as ShadowsOnly gives the Beam one clean occluder without letting
            // walls and ceilings double-project their already baked shadows.
            shadowCasterRenderer.enabled = true;
            shadowCasterRenderer.receiveShadows = false;
            shadowCasterRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            shadowCasterRenderer.renderingLayerMask =
                (uint)RoomLocalLightShareContract.DoorShadowRenderingLayerMask;
            failure = null;
            return true;
        }

        private bool ApplyVisibleSurfacePolicy()
        {
            return ApplyVisibleSurfacePolicy(out _);
        }

        private bool ApplyVisibleSurfacePolicy(out string failure)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                Renderer renderer = bindings[i].Renderer;
                if (renderer == null)
                    continue;
                renderer.renderingLayerMask = RoomLocalRenderingLayers.DoorVisibleMask(
                    (uint)RoomLocalLightShareContract.DoorSurfaceRenderingLayerMask);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
            }

            if (enablePerFaceDirectResponse)
                return TryConfigureRuntimeEmissionMaterial(out failure);

            ClearRuntimeEmissionMaterial();
            failure = null;
            return true;
        }

        private void ClearRuntimeEmissionMaterial()
        {
            for (int i = 0; i < bindings.Count; i++)
                bindings[i].RestoreOriginalSharedMaterials();
            if (runtimeOpaqueMaterial == null)
                return;
            Destroy(runtimeOpaqueMaterial);
            runtimeOpaqueMaterial = null;
        }

        private void ApplyProbe(
            RendererBinding binding,
            SphericalHarmonicsL2 probe,
            Vector4 occlusion,
            Color directResponse)
        {
            Renderer renderer = binding.Renderer;
            renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
            singleProbe[0] = probe;
            singleOcclusion[0] = occlusion;

            renderer.GetPropertyBlock(workingBlock);
            CopyProbeToWorkingBlock();
            renderer.SetPropertyBlock(workingBlock);
            for (int materialIndex = 0; materialIndex < binding.MaterialCount; materialIndex++)
            {
                renderer.GetPropertyBlock(workingBlock, materialIndex);
                CopyProbeToWorkingBlock();
                if (materialIndex == 0 && enablePerFaceDirectResponse)
                    workingBlock.SetColor(EmissionColorId, directResponse);
                renderer.SetPropertyBlock(workingBlock, materialIndex);
            }
        }

        private Color EvaluatePerFaceDirectResponse(Vector3 worldNormal)
        {
            float startPower = startLighting.CurrentPowerLevel ==
                               DungeonTileLightmapSwitcher.PowerLevel.P100
                ? 1f
                : 0f;
            float administrativePower = administrativeLighting.CurrentPowerLevel ==
                                        DungeonTileLightmapSwitcher.PowerLevel.P100
                ? 1f
                : 0f;
            float aperture = 1f;
            if (connection != null)
            {
                RoomLocalTransferState state = connection.EvaluateTransferState();
                if (!state.Enabled)
                    return Color.black;
                startPower = state.StartPower01;
                administrativePower = state.AdministrativePower01;
                aperture = state.ApertureFraction;
            }
            Vector3 directionToStart = -startDoorway.forward;
            Vector3 directionToAdministrative = -administrativeDoorway.forward;
            float startFacing = RoomLocalLightShareMath.ComputeLambertFaceResponse(
                worldNormal,
                directionToStart);
            float administrativeFacing = RoomLocalLightShareMath.ComputeLambertFaceResponse(
                worldNormal,
                directionToAdministrative);
            float tuning = connection != null ? connection.DirectIntensityMultiplier : 1f;

            Color response =
                PoweredDirectColor(startOutgoing, startPower) * startFacing +
                PoweredDirectColor(administrativeOutgoing, administrativePower) *
                administrativeFacing;
            response *= perFaceDirectScale * tuning * aperture;
            response.a = 1f;
            return response;
        }

        private static Color PoweredDirectColor(OutgoingPortalMap outgoing, float power01)
        {
            if (outgoing == null || power01 <= 0f)
                return Color.black;

            Color delta = new Color(
                Mathf.Max(0f, outgoing.Power100Average.r - outgoing.Power0Average.r),
                Mathf.Max(0f, outgoing.Power100Average.g - outgoing.Power0Average.g),
                Mathf.Max(0f, outgoing.Power100Average.b - outgoing.Power0Average.b),
                1f);
            float maximum = Mathf.Max(delta.r, Mathf.Max(delta.g, delta.b));
            if (maximum <= 0.00001f)
                return Color.black;

            return new Color(
                delta.r / maximum,
                delta.g / maximum,
                delta.b / maximum,
                1f) * outgoing.Power100DirectPeak * Mathf.Clamp01(power01);
        }

        private bool TryConfigureRuntimeEmissionMaterial(out string failure)
        {
            failure = null;
            if (!enablePerFaceDirectResponse)
                return true;
            if (runtimeOpaqueMaterial != null)
                return true;
            if (bindings.Count == 0 || bindings[0].Renderer == null ||
                bindings[0].Renderer.sharedMaterials == null ||
                bindings[0].Renderer.sharedMaterials.Length == 0 ||
                bindings[0].Renderer.sharedMaterials[0] == null)
            {
                failure = "Door probe driver found no opaque door material for direct response.";
                return false;
            }

            runtimeOpaqueMaterial = new Material(bindings[0].Renderer.sharedMaterials[0])
            {
                name = bindings[0].Renderer.sharedMaterials[0].name +
                       "_RuntimePerFaceDirect",
                hideFlags = HideFlags.HideAndDontSave,
                globalIlluminationFlags = MaterialGlobalIlluminationFlags.None
            };
            runtimeOpaqueMaterial.EnableKeyword("_EMISSION");
            runtimeOpaqueMaterial.SetColor(EmissionColorId, Color.black);
            for (int i = 0; i < bindings.Count; i++)
                bindings[i].AssignRuntimeOpaqueMaterial(runtimeOpaqueMaterial);
            return true;
        }

        private void CopyProbeToWorkingBlock()
        {
            workingBlock.CopySHCoefficientArraysFrom(singleProbe);
            workingBlock.CopyProbeOcclusionArrayFrom(singleOcclusion);
        }

        private void RestoreOriginalState()
        {
            // OnDisable restores before the installer hands SH back to its prior receiver.
            // A later OnDestroy must not overwrite that receiver's newly applied block.
            if (!restorationPending)
                return;
            restorationPending = false;
            for (int i = 0; i < bindings.Count; i++)
                bindings[i].RestoreOriginalState();
            if (runtimeOpaqueMaterial != null)
            {
                Destroy(runtimeOpaqueMaterial);
                runtimeOpaqueMaterial = null;
            }
            DestroyRuntimeEdgeBindings();
            if (shadowCasterStateCaptured && shadowCasterRenderer != null)
            {
                shadowCasterRenderer.enabled = originalShadowCasterEnabled;
                shadowCasterRenderer.receiveShadows = originalShadowCasterReceiveShadows;
                shadowCasterRenderer.shadowCastingMode = originalShadowCastingMode;
                shadowCasterRenderer.renderingLayerMask =
                    originalShadowCasterRenderingLayerMask;
            }
            applied = false;
            initialized = false;
        }

        private void SubscribePowerEvents()
        {
            UnsubscribePowerEvents();
            if (startLighting != null)
                startLighting.PowerLevelApplied += OnPowerChanged;
            if (administrativeLighting != null && administrativeLighting != startLighting)
                administrativeLighting.PowerLevelApplied += OnPowerChanged;
        }

        private void UnsubscribePowerEvents()
        {
            if (startLighting != null)
                startLighting.PowerLevelApplied -= OnPowerChanged;
            if (administrativeLighting != null && administrativeLighting != startLighting)
                administrativeLighting.PowerLevelApplied -= OnPowerChanged;
        }

        private void OnPowerChanged(DungeonTileLightmapSwitcher.PowerLevel power)
        {
            if (!faultLatched)
                TryApplyNow(out _);
        }

        private void CacheRoomTiles()
        {
            startTile = startRoot != null ? startRoot.GetComponentInParent<DunGen.Tile>(true) : null;
            administrativeTile = administrativeRoot != null
                ? administrativeRoot.GetComponentInParent<DunGen.Tile>(true)
                : null;
        }

        private bool TrySample(
            Transform roomRoot,
            DungeonTileBakeData bake,
            Vector3 worldPosition,
            out SphericalHarmonicsL2 probe,
            out Vector4 occlusion,
            out float nearestDistance)
        {
            probe = default;
            occlusion = Vector4.one;
            nearestDistance = float.PositiveInfinity;
            DungeonTileProbeRegistry activeRegistry = DungeonTileProbeRegistry.Active;
            if (activeRegistry != null)
            {
                DunGen.Tile tile = roomRoot == startRoot ? startTile :
                    roomRoot == administrativeRoot ? administrativeTile : null;
                if (tile == null)
                    return false;
                if (activeRegistry.TrySampleForTile(tile, worldPosition, out probe, out occlusion,
                        out DungeonTileProbeRegistry.SampleInfo info, doorLeaf))
                    return true;

                // Visibility failure is a valid dark result, never a request to revive the
                // old distance-only sample. Missing registered data remains a query failure.
                probe = default;
                occlusion = Vector4.one;
                return info.hasTileData && info.noVisibleProbes;
            }

            if (roomRoot == null || !HasProbeData(bake))
                return false;
            DungeonTileBakeData.LightProbeBakeEntry[] entries = bake.lightProbeEntries;
            ResetNearestBuffers();
            for (int i = 0; i < entries.Length; i++)
            {
                Vector3 samplePosition = roomRoot.TransformPoint(entries[i].localPosition);
                InsertNearestProbe(i, (samplePosition - worldPosition).sqrMagnitude);
            }

            float totalWeight = 0f;
            Vector4 blendedOcclusion = Vector4.zero;
            for (int i = 0; i < BlendProbeCount; i++)
            {
                int sampleIndex = bestIndices[i];
                if (sampleIndex < 0)
                    continue;
                float distance = Mathf.Sqrt(Mathf.Max(0f, bestDistances[i]));
                float weight = 1f / Mathf.Max(0.05f, distance + 0.05f);
                AddWeighted(ref probe, entries[sampleIndex].ToSphericalHarmonics(), weight);
                blendedOcclusion += entries[sampleIndex].occlusion * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0f)
                return false;
            nearestDistance = Mathf.Sqrt(Mathf.Max(0f, bestDistances[0]));
            float inverseWeight = 1f / totalWeight;
            Scale(ref probe, inverseWeight);
            occlusion = blendedOcclusion * inverseWeight;
            return true;
        }

        private void ResetNearestBuffers()
        {
            for (int i = 0; i < BlendProbeCount; i++)
            {
                bestDistances[i] = float.PositiveInfinity;
                bestIndices[i] = -1;
            }
        }

        private void InsertNearestProbe(int probeIndex, float sqrDistance)
        {
            for (int i = 0; i < BlendProbeCount; i++)
            {
                if (sqrDistance >= bestDistances[i])
                    continue;
                for (int j = BlendProbeCount - 1; j > i; j--)
                {
                    bestDistances[j] = bestDistances[j - 1];
                    bestIndices[j] = bestIndices[j - 1];
                }
                bestDistances[i] = sqrDistance;
                bestIndices[i] = probeIndex;
                return;
            }
        }

        private static DungeonTileBakeData CurrentBake(DungeonTileLightmapSwitcher switcher)
        {
            return switcher != null ? switcher.GetBakeData(switcher.CurrentPowerLevel) : null;
        }

        private static bool HasProbeData(DungeonTileBakeData bake)
        {
            return bake != null && bake.lightProbeEntries != null &&
                   bake.lightProbeEntries.Length > 0;
        }

        private static Vector3 ResolveWorldNormal(RendererBinding binding)
        {
            Vector3 normal = binding.GroupTransform.TransformDirection(binding.LocalNormal);
            return normal.sqrMagnitude > 0.00001f ? normal.normalized : Vector3.forward;
        }

        private Vector3 ResolveClosedWorldNormal(Transform groupTransform, Vector3 localNormal)
        {
            Vector3 currentWorld = groupTransform != null
                ? groupTransform.TransformDirection(localNormal)
                : localNormal;
            if (doorLeaf == null)
                return currentWorld.sqrMagnitude > 0.00001f ? currentWorld.normalized : Vector3.forward;

            Quaternion closedLocal = connection != null && connection.DoorAngle != null
                ? connection.DoorAngle.ClosedLocalRotation
                : doorLeaf.localRotation;
            return RoomLocalLightShareMath.ClosedWorldDirection(
                currentWorld,
                doorLeaf.rotation,
                RoomLocalLightShareMath.LeafWorldRotation(doorLeaf, closedLocal));
        }

        private bool OwnsStartRoom(Vector3 closedWorldNormal)
        {
            return RoomLocalLightShareMath.FaceOwnsFirstRoom(
                closedWorldNormal,
                -startDoorway.forward,
                -administrativeDoorway.forward);
        }

        private uint ResolveCookieReceiveLayer(RendererBinding binding, Vector3 center)
        {
            Vector3 sample = center + ResolveWorldNormal(binding) * sideSampleOffset;
            float side = RoomLocalLightShareMath.DoorwayPlaneSide(
                sample,
                startDoorway.position,
                -startDoorway.forward);
            return RoomLocalRenderingLayers.DoorVisibleMask(
                RoomLocalLightShareMath.DoorCookieReceiveLayer(
                    side,
                    locationConfidenceDistance));
        }

        private bool TrySampleAlongCurrentNormal(
            RendererBinding binding,
            Vector3 center,
            bool useClosedPrior,
            Transform firstRoot,
            DungeonTileBakeData firstBake,
            Transform secondRoot,
            DungeonTileBakeData secondBake,
            out SphericalHarmonicsL2 probe,
            out Vector4 occlusion,
            out float startWeight,
            out string failure)
        {
            Vector3 worldNormal = ResolveWorldNormal(binding);
            Vector3 samplePosition = center + worldNormal * sideSampleOffset;
            float side = RoomLocalLightShareMath.DoorwayPlaneSide(
                samplePosition,
                startDoorway.position,
                -startDoorway.forward);
            float locationWeight = RoomLocalLightShareMath.DoorwayPlaneRoomWeight(
                side,
                planeBlendDistance);
            startWeight = useClosedPrior
                ? RoomLocalLightShareMath.BlendClosedAndLocationRoomWeight(
                    binding.OwnsStartRoom,
                    locationWeight,
                    RoomLocalLightShareMath.LocationConfidence(side, locationConfidenceDistance))
                : locationWeight;

            if (!TrySample(
                    firstRoot,
                    firstBake,
                    samplePosition,
                    out SphericalHarmonicsL2 startProbe,
                    out Vector4 startOcclusion,
                    out _) ||
                !TrySample(
                    secondRoot,
                    secondBake,
                    samplePosition,
                    out SphericalHarmonicsL2 administrativeProbe,
                    out Vector4 administrativeOcclusion,
                    out _))
            {
                failure = "Door surface could not sample both room probe sets.";
                probe = default;
                occlusion = Vector4.one;
                return false;
            }

            probe = Blend(startProbe, administrativeProbe, startWeight);
            occlusion = Vector4.Lerp(administrativeOcclusion, startOcclusion, startWeight);
            failure = null;
            return true;
        }

        private bool TryCreateDirectionalEdgeBindings(
            Renderer sourceRenderer,
            Transform orientationRoot,
            out string failure)
        {
            failure = null;
            var filter = sourceRenderer.GetComponent<MeshFilter>();
            Mesh sourceMesh = filter != null ? filter.sharedMesh : null;
            if (sourceMesh == null || sourceMesh.vertexCount == 0 || sourceMesh.subMeshCount == 0)
            {
                failure = "Door edge renderer has no readable mesh for directional splitting.";
                return false;
            }

            Vector3[] vertices = sourceMesh.vertices;
            var triangles = new List<int>[EdgeDirections.Length, sourceMesh.subMeshCount];
            var normalSums = new Vector3[EdgeDirections.Length];
            var triangleCounts = new int[EdgeDirections.Length];
            for (int direction = 0; direction < EdgeDirections.Length; direction++)
            for (int submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
                triangles[direction, submesh] = new List<int>();

            for (int submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
            {
                int[] sourceTriangles = sourceMesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < sourceTriangles.Length; i += 3)
                {
                    int a = sourceTriangles[i];
                    int b = sourceTriangles[i + 1];
                    int c = sourceTriangles[i + 2];
                    Vector3 areaNormal = Vector3.Cross(
                        vertices[b] - vertices[a],
                        vertices[c] - vertices[a]);
                    if (areaNormal.sqrMagnitude <= 0.0000001f)
                        continue;

                    Vector3 faceNormal = areaNormal.normalized;
                    int directionIndex = ClosestEdgeDirection(faceNormal);
                    triangles[directionIndex, submesh].Add(a);
                    triangles[directionIndex, submesh].Add(b);
                    triangles[directionIndex, submesh].Add(c);
                    normalSums[directionIndex] += areaNormal;
                    triangleCounts[directionIndex]++;
                }
            }

            int created = 0;
            for (int direction = 0; direction < EdgeDirections.Length; direction++)
            {
                if (triangleCounts[direction] == 0)
                    continue;

                Mesh mesh = Instantiate(sourceMesh);
                mesh.name = sourceMesh.name + "_Runtime_" + EdgeDirectionNames[direction];
                for (int submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
                    mesh.SetTriangles(triangles[direction, submesh], submesh, false);
                mesh.RecalculateBounds();

                var child = new GameObject("DungeonDoorProbe_Edge_" + EdgeDirectionNames[direction]);
                child.transform.SetParent(sourceRenderer.transform, false);
                child.transform.localPosition = Vector3.zero;
                child.transform.localRotation = Quaternion.identity;
                child.transform.localScale = Vector3.one;
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = child.AddComponent<MeshRenderer>();
                CopyRendererSettings(sourceRenderer, renderer);

                Vector3 localNormal = normalSums[direction].sqrMagnitude > 0.00001f
                    ? normalSums[direction].normalized
                    : EdgeDirections[direction];
                Vector3 closedWorldNormal = ResolveClosedWorldNormal(orientationRoot, localNormal);
                bindings.Add(new RendererBinding(
                    renderer,
                    DungeonDoorProbeRendererGroup.Group.Edge,
                    orientationRoot,
                    localNormal,
                    closedWorldNormal,
                    OwnsStartRoom(closedWorldNormal)));
                runtimeEdgeObjects.Add(child);
                runtimeEdgeMeshes.Add(mesh);
                created++;
            }

            if (created < 2)
            {
                failure = "Door edge mesh did not produce enough directional surface groups.";
                return false;
            }

            runtimeEdgeSources.Add(sourceRenderer);
            sourceRenderer.enabled = false;
            return true;
        }

        private static int ClosestEdgeDirection(Vector3 faceNormal)
        {
            int bestIndex = 0;
            float bestDot = float.NegativeInfinity;
            for (int i = 0; i < EdgeDirections.Length; i++)
            {
                float dot = Vector3.Dot(faceNormal, EdgeDirections[i]);
                if (dot <= bestDot)
                    continue;
                bestDot = dot;
                bestIndex = i;
            }
            return bestIndex;
        }

        private static void CopyRendererSettings(Renderer source, MeshRenderer destination)
        {
            destination.sharedMaterials = source.sharedMaterials;
            destination.shadowCastingMode = source.shadowCastingMode;
            destination.receiveShadows = source.receiveShadows;
            destination.lightProbeUsage = source.lightProbeUsage;
            destination.reflectionProbeUsage = source.reflectionProbeUsage;
            destination.probeAnchor = source.probeAnchor;
            destination.lightProbeProxyVolumeOverride = source.lightProbeProxyVolumeOverride;
            destination.renderingLayerMask = source.renderingLayerMask;
            destination.rendererPriority = source.rendererPriority;
            destination.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
            destination.motionVectorGenerationMode = source.motionVectorGenerationMode;
        }

        private void DestroyRuntimeEdgeBindings()
        {
            for (int i = 0; i < runtimeEdgeSources.Count; i++)
            {
                if (runtimeEdgeSources[i] != null)
                    runtimeEdgeSources[i].enabled = true;
            }
            runtimeEdgeSources.Clear();

            for (int i = 0; i < runtimeEdgeObjects.Count; i++)
            {
                if (runtimeEdgeObjects[i] != null)
                    Destroy(runtimeEdgeObjects[i]);
            }
            runtimeEdgeObjects.Clear();

            for (int i = 0; i < runtimeEdgeMeshes.Count; i++)
            {
                if (runtimeEdgeMeshes[i] != null)
                    Destroy(runtimeEdgeMeshes[i]);
            }
            runtimeEdgeMeshes.Clear();
        }

        private static SphericalHarmonicsL2 Blend(
            SphericalHarmonicsL2 start,
            SphericalHarmonicsL2 administrative,
            float startWeight)
        {
            SphericalHarmonicsL2 result = default;
            float clamped = Mathf.Clamp01(startWeight);
            for (int channel = 0; channel < 3; channel++)
            for (int coefficient = 0; coefficient < 9; coefficient++)
                result[channel, coefficient] = Mathf.Lerp(
                    administrative[channel, coefficient],
                    start[channel, coefficient],
                    clamped);
            return result;
        }

        private static void AddWeighted(
            ref SphericalHarmonicsL2 destination,
            SphericalHarmonicsL2 source,
            float weight)
        {
            for (int channel = 0; channel < 3; channel++)
            for (int coefficient = 0; coefficient < 9; coefficient++)
                destination[channel, coefficient] += source[channel, coefficient] * weight;
        }

        private static void Scale(ref SphericalHarmonicsL2 probe, float scale)
        {
            for (int channel = 0; channel < 3; channel++)
            for (int coefficient = 0; coefficient < 9; coefficient++)
                probe[channel, coefficient] *= scale;
        }

        private static float L0Luminance(SphericalHarmonicsL2 probe)
        {
            return probe[0, 0] * 0.2126f + probe[1, 0] * 0.7152f +
                   probe[2, 0] * 0.0722f;
        }

        // Every door in the dungeon gets its own driver, so the same fault (typically "no P0/P100 bake
        // data yet") would otherwise be reported once per door. Report each distinct reason once per session.
        private static readonly System.Collections.Generic.HashSet<string> s_reportedFaults = new();

        private void LatchFault(string reason)
        {
            if (faultLatched)
                return;
            faultLatched = true;
            faultReason = string.IsNullOrWhiteSpace(reason) ? "Unknown door probe fault." : reason;
            RestoreOriginalState();

            if (s_reportedFaults.Add(faultReason))
                Debug.LogWarning("[RoomLocalDoorProbeDriver] " + faultReason + " (further doors with the same fault stay silent this session)", this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetReportedFaults()
        {
            s_reportedFaults.Clear();
        }

        private sealed class RendererBinding
        {
            public readonly Renderer Renderer;
            public readonly DungeonDoorProbeRendererGroup.Group Group;
            public readonly Transform GroupTransform;
            public readonly Vector3 LocalNormal;
            public readonly Vector3 ClosedWorldNormal;
            public readonly bool OwnsStartRoom;
            public readonly int MaterialCount;

            private LightProbeUsage originalLightProbeUsage;
            private uint originalRenderingLayerMask;
            private ShadowCastingMode originalShadowCastingMode;
            private bool originalReceiveShadows;
            private Material[] originalSharedMaterials;
            private MaterialPropertyBlock originalGlobalBlock;
            private bool originalGlobalEmpty;
            private MaterialPropertyBlock[] originalMaterialBlocks;
            private bool[] originalMaterialEmpty;
            private bool captured;

            public RendererBinding(
                Renderer renderer,
                DungeonDoorProbeRendererGroup.Group group,
                Transform groupTransform,
                Vector3 localNormal,
                Vector3 closedWorldNormal,
                bool ownsStartRoom)
            {
                Renderer = renderer;
                Group = group;
                GroupTransform = groupTransform;
                LocalNormal = localNormal;
                ClosedWorldNormal = closedWorldNormal.sqrMagnitude > 0.00001f
                    ? closedWorldNormal.normalized
                    : Vector3.forward;
                OwnsStartRoom = ownsStartRoom;
                MaterialCount = renderer.sharedMaterials != null
                    ? renderer.sharedMaterials.Length
                    : 0;
            }

            public void CaptureOriginalState()
            {
                if (captured || Renderer == null)
                    return;
                captured = true;
                originalLightProbeUsage = Renderer.lightProbeUsage;
                originalRenderingLayerMask = Renderer.renderingLayerMask;
                originalShadowCastingMode = Renderer.shadowCastingMode;
                originalReceiveShadows = Renderer.receiveShadows;
                originalSharedMaterials = Renderer.sharedMaterials;
                originalGlobalBlock = new MaterialPropertyBlock();
                Renderer.GetPropertyBlock(originalGlobalBlock);
                originalGlobalEmpty = originalGlobalBlock.isEmpty;
                originalMaterialBlocks = new MaterialPropertyBlock[MaterialCount];
                originalMaterialEmpty = new bool[MaterialCount];
                for (int i = 0; i < MaterialCount; i++)
                {
                    var block = new MaterialPropertyBlock();
                    Renderer.GetPropertyBlock(block, i);
                    originalMaterialBlocks[i] = block;
                    originalMaterialEmpty[i] = block.isEmpty;
                }
            }

            public void AssignRuntimeOpaqueMaterial(Material material)
            {
                if (Renderer == null || material == null || Renderer.sharedMaterials.Length == 0)
                    return;
                Material[] materials = Renderer.sharedMaterials;
                materials[0] = material;
                Renderer.sharedMaterials = materials;
            }

            public void RestoreOriginalSharedMaterials()
            {
                if (Renderer != null && originalSharedMaterials != null)
                    Renderer.sharedMaterials = originalSharedMaterials;
            }

            public void RestoreOriginalState()
            {
                if (!captured || Renderer == null)
                    return;
                Renderer.lightProbeUsage = originalLightProbeUsage;
                Renderer.renderingLayerMask = originalRenderingLayerMask;
                Renderer.shadowCastingMode = originalShadowCastingMode;
                Renderer.receiveShadows = originalReceiveShadows;
                if (originalSharedMaterials != null)
                    Renderer.sharedMaterials = originalSharedMaterials;
                Renderer.SetPropertyBlock(originalGlobalEmpty ? null : originalGlobalBlock);
                for (int i = 0; i < MaterialCount; i++)
                {
                    Renderer.SetPropertyBlock(
                        originalMaterialEmpty[i] ? null : originalMaterialBlocks[i],
                        i);
                }
            }
        }
    }
}
