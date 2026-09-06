using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonPortalTransportPoC.Editor
{
    /// <summary>
    /// Read-only renderer/material state capture for the isolated portal transport PoC.
    /// The only writes performed by this utility are explicit report writes under Evidence.
    /// </summary>
    public sealed class DungeonPortalRendererParityAuditWindow : EditorWindow
    {
        private const string EvidenceAssetDirectory =
            "Assets/Experiments/DungeonPortalTransportPoC/Evidence";
        private const string BaselineJsonAssetPath =
            EvidenceAssetDirectory + "/RendererParityBaseline.json";
        private const string BaselineTextAssetPath =
            EvidenceAssetDirectory + "/RendererParityBaseline.txt";
        private const string ComparisonJsonAssetPath =
            EvidenceAssetDirectory + "/RendererParityComparison.json";
        private const string ComparisonTextAssetPath =
            EvidenceAssetDirectory + "/RendererParityComparison.txt";

        [SerializeField] private List<GameObject> roots = new List<GameObject>();
        [SerializeField] private Vector2 scroll;

        [MenuItem("Tools/Dungeon/Lighting/Portal Transport PoC/Renderer Parity Audit")]
        private static void Open()
        {
            var window = GetWindow<DungeonPortalRendererParityAuditWindow>();
            window.titleContent = new GUIContent("Portal Renderer Parity");
            window.minSize = new Vector2(560f, 420f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Read-only audit. It never edits renderers, materials, property blocks, transforms, " +
                "scenes, or Play Mode. Reports are written only when a button below is pressed.",
                MessageType.Info);

            EditorGUILayout.LabelField("Audit roots", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Every Renderer below each root is captured, including inactive children. " +
                "Nested selected roots are collapsed to their selected ancestor.",
                EditorStyles.wordWrappedMiniLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(170f));
            for (int i = 0; i < roots.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                roots[i] = (GameObject)EditorGUILayout.ObjectField(
                    $"Root {i + 1}", roots[i], typeof(GameObject), true);
                if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                {
                    roots.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add selected GameObjects"))
                AddSelectedRoots();
            if (GUILayout.Button("Clear roots"))
                roots.Clear();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Baseline",
                File.Exists(ToAbsolutePath(BaselineJsonAssetPath))
                    ? BaselineJsonAssetPath
                    : "No baseline report found.");

            using (new EditorGUI.DisabledScope(NormalizeRoots(roots).Count == 0))
            {
                if (GUILayout.Button("Capture baseline from roots"))
                    CaptureBaseline();
            }

            using (new EditorGUI.DisabledScope(
                       !File.Exists(ToAbsolutePath(BaselineJsonAssetPath))))
            {
                if (GUILayout.Button("Compare current against baseline"))
                    CompareCurrent();
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "If the root list is empty during comparison, the tool resolves the roots stored " +
                "in the baseline by GlobalObjectId. Runtime-only objects cannot always be resolved " +
                "after leaving Play Mode or reloading their scene.",
                MessageType.None);
        }

        private void AddSelectedRoots()
        {
            GameObject[] selected = Selection.gameObjects;
            for (int i = 0; i < selected.Length; i++)
            {
                GameObject candidate = selected[i];
                if (candidate != null && !roots.Contains(candidate))
                    roots.Add(candidate);
            }

            roots = NormalizeRoots(roots);
            Repaint();
        }

        private void CaptureBaseline()
        {
            try
            {
                List<GameObject> normalizedRoots = NormalizeRoots(roots);
                if (normalizedRoots.Count == 0)
                    throw new InvalidOperationException("At least one valid audit root is required.");

                RendererParitySnapshot baseline = RendererParityCapture.Capture(normalizedRoots);
                RendererParityReportWriter.WriteBaseline(baseline);
                Debug.Log(
                    $"Portal renderer parity baseline captured: renderers={baseline.renderers.Count}, " +
                    $"duplicates={baseline.duplicateGroups.Count}\n{BaselineJsonAssetPath}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Renderer parity capture failed", exception.Message, "OK");
            }
        }

        private void CompareCurrent()
        {
            try
            {
                RendererParitySnapshot baseline = RendererParityReportWriter.ReadBaseline();
                List<GameObject> normalizedRoots = NormalizeRoots(roots);
                if (normalizedRoots.Count == 0)
                    normalizedRoots = ResolveBaselineRoots(baseline);
                if (normalizedRoots.Count == 0)
                {
                    throw new InvalidOperationException(
                        "None of the baseline roots can be resolved. Re-add the live roots explicitly.");
                }

                RendererParitySnapshot current = RendererParityCapture.Capture(normalizedRoots);
                RendererParityComparison comparison =
                    RendererParityComparer.Compare(baseline, current);
                RendererParityReportWriter.WriteComparison(comparison);

                string severity = comparison.HasParityFailure ? "FAIL" : "PASS";
                string message =
                    $"{severity} portal renderer parity: unchanged={comparison.unchangedCount}, " +
                    $"changed={comparison.changed.Count}, added={comparison.added.Count}, " +
                    $"removed={comparison.removed.Count}, " +
                    $"newDuplicateGroups={comparison.newDuplicateGroups.Count}\n" +
                    ComparisonJsonAssetPath;
                if (comparison.HasParityFailure)
                    Debug.LogError(message);
                else
                    Debug.Log(message);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Renderer parity comparison failed", exception.Message, "OK");
            }
        }

        private static List<GameObject> ResolveBaselineRoots(RendererParitySnapshot baseline)
        {
            var resolved = new List<GameObject>();
            for (int i = 0; i < baseline.roots.Count; i++)
            {
                string globalIdText = baseline.roots[i].globalObjectId;
                if (string.IsNullOrEmpty(globalIdText) ||
                    !GlobalObjectId.TryParse(globalIdText, out GlobalObjectId globalId))
                {
                    continue;
                }

                GameObject root = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId) as GameObject;
                if (root != null)
                    resolved.Add(root);
            }

            return NormalizeRoots(resolved);
        }

        private static List<GameObject> NormalizeRoots(IEnumerable<GameObject> candidates)
        {
            var unique = candidates
                .Where(candidate => candidate != null)
                .Distinct()
                .OrderBy(RendererParityCapture.GetGameObjectPath, StringComparer.Ordinal)
                .ToList();

            var result = new List<GameObject>();
            for (int i = 0; i < unique.Count; i++)
            {
                Transform candidate = unique[i].transform;
                bool nested = unique.Any(
                    other => other != unique[i] && candidate.IsChildOf(other.transform));
                if (!nested)
                    result.Add(unique[i]);
            }

            return result;
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        [Serializable]
        internal sealed class RendererParitySnapshot
        {
            public int formatVersion = 1;
            public string unityVersion;
            public List<AuditRootSnapshot> roots = new List<AuditRootSnapshot>();
            public List<RendererSnapshot> renderers = new List<RendererSnapshot>();
            public List<DuplicateRendererGroup> duplicateGroups = new List<DuplicateRendererGroup>();
        }

        [Serializable]
        internal sealed class AuditRootSnapshot
        {
            public string globalObjectId;
            public string path;
        }

        [Serializable]
        internal sealed class RendererSnapshot
        {
            public string id;
            public string path;
            public string gameObjectId;
            public string rendererType;
            public bool activeInHierarchy;
            public bool enabled;
            public bool forceRenderingOff;
            public string meshId;
            public string meshName;
            public int meshVertexCount;
            public int meshSubMeshCount;
            public string additionalVertexStreamsId;
            public string additionalVertexStreamsName;
            public string localToWorldMatrix;
            public List<MaterialSnapshot> materials = new List<MaterialSnapshot>();
            public int lightmapIndex;
            public string lightmapScaleOffset;
            public int realtimeLightmapIndex;
            public string realtimeLightmapScaleOffset;
            public string lightProbeUsage;
            public string reflectionProbeUsage;
            public string probeAnchorId;
            public string probeAnchorPath;
            public string lightProbeProxyVolumeOverrideId;
            public string lightProbeProxyVolumeOverridePath;
            public string shadowCastingMode;
            public bool receiveShadows;
            public string motionVectorGenerationMode;
            public bool allowOcclusionWhenDynamic;
            public bool staticShadowCaster;
            public int rendererPriority;
            public uint renderingLayerMask;
            public int sortingLayerId;
            public string sortingLayerName;
            public int sortingOrder;
            public RendererPropertyBlockSnapshot rendererPropertyBlock;
            public List<RendererPropertyBlockSnapshot> materialPropertyBlocks =
                new List<RendererPropertyBlockSnapshot>();
        }

        [Serializable]
        internal sealed class MaterialSnapshot
        {
            public int slot;
            public bool isNull;
            public string id;
            public string name;
            public string assetPath;
            public string shaderId;
            public string shaderName;
            public string shaderAssetPath;
            public int renderQueue;
            public int rawRenderQueue;
            public int shaderDefaultRenderQueue;
            public string renderTypeTag;
            public string queueTag;
            public string renderPipelineTag;
            public bool enableInstancing;
            public bool doubleSidedGi;
            public string globalIlluminationFlags;
            public List<string> keywords = new List<string>();
            public List<ShaderPassSnapshot> passes = new List<ShaderPassSnapshot>();
            public List<ShaderPropertySnapshot> properties = new List<ShaderPropertySnapshot>();
        }

        [Serializable]
        internal sealed class ShaderPassSnapshot
        {
            public int index;
            public string name;
            public bool enabled;
        }

        [Serializable]
        internal sealed class ShaderPropertySnapshot
        {
            public int index;
            public string name;
            public string description;
            public string type;
            public string flags;
            public string value;
            public string textureId;
            public string textureName;
            public string textureScale;
            public string textureOffset;
        }

        [Serializable]
        internal sealed class RendererPropertyBlockSnapshot
        {
            public string scope;
            public int materialIndex;
            public bool isEmpty;
            public List<PropertyBlockValueSnapshot> properties =
                new List<PropertyBlockValueSnapshot>();
        }

        [Serializable]
        internal sealed class PropertyBlockValueSnapshot
        {
            public string name;
            public string type;
            public bool hasOverride;
            public string value;
            public string textureId;
            public string textureName;
        }

        [Serializable]
        internal sealed class DuplicateRendererGroup
        {
            public string signature;
            public string meshId;
            public string localToWorldMatrix;
            public List<string> rendererIds = new List<string>();
            public List<string> rendererPaths = new List<string>();
        }

        [Serializable]
        internal sealed class RendererParityComparison
        {
            public int formatVersion = 1;
            public bool HasParityFailure;
            public int unchangedCount;
            public RendererParitySnapshot baseline;
            public RendererParitySnapshot current;
            public List<RendererChangeSnapshot> changed = new List<RendererChangeSnapshot>();
            public List<RendererSnapshot> added = new List<RendererSnapshot>();
            public List<RendererSnapshot> removed = new List<RendererSnapshot>();
            public List<DuplicateRendererGroup> newDuplicateGroups =
                new List<DuplicateRendererGroup>();
            public List<DuplicateRendererGroup> resolvedDuplicateGroups =
                new List<DuplicateRendererGroup>();
        }

        [Serializable]
        internal sealed class RendererChangeSnapshot
        {
            public string id;
            public string path;
            public List<string> changedSections = new List<string>();
            public RendererSnapshot baseline;
            public RendererSnapshot current;
        }

        internal static class RendererParityCapture
        {
            private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

            public static RendererParitySnapshot Capture(IReadOnlyList<GameObject> rootsToCapture)
            {
                var snapshot = new RendererParitySnapshot
                {
                    unityVersion = Application.unityVersion
                };

                for (int i = 0; i < rootsToCapture.Count; i++)
                {
                    GameObject root = rootsToCapture[i];
                    snapshot.roots.Add(new AuditRootSnapshot
                    {
                        globalObjectId = GetGlobalObjectId(root),
                        path = GetGameObjectPath(root)
                    });
                }

                snapshot.roots = snapshot.roots
                    .OrderBy(root => root.path, StringComparer.Ordinal)
                    .ToList();

                var seenRenderers = new HashSet<Renderer>();
                for (int rootIndex = 0; rootIndex < rootsToCapture.Count; rootIndex++)
                {
                    Renderer[] renderers = rootsToCapture[rootIndex]
                        .GetComponentsInChildren<Renderer>(true);
                    for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                    {
                        Renderer renderer = renderers[rendererIndex];
                        if (renderer != null && seenRenderers.Add(renderer))
                            snapshot.renderers.Add(CaptureRenderer(renderer));
                    }
                }

                snapshot.renderers = snapshot.renderers
                    .OrderBy(renderer => renderer.id, StringComparer.Ordinal)
                    .ThenBy(renderer => renderer.path, StringComparer.Ordinal)
                    .ToList();
                snapshot.duplicateGroups = FindDuplicateGroups(snapshot.renderers);
                return snapshot;
            }

            public static string GetGameObjectPath(GameObject gameObject)
            {
                if (gameObject == null)
                    return "<null>";

                string scenePath = gameObject.scene.IsValid()
                    ? NormalizePath(string.IsNullOrEmpty(gameObject.scene.path)
                        ? gameObject.scene.name
                        : gameObject.scene.path)
                    : "<not-in-scene>";
                return scenePath + "::" + GetTransformPath(gameObject.transform);
            }

            private static RendererSnapshot CaptureRenderer(Renderer renderer)
            {
                Mesh mesh = GetRendererMesh(renderer);
                Mesh additionalVertexStreams =
                    renderer is MeshRenderer meshRenderer ? meshRenderer.additionalVertexStreams : null;
                Material[] materials = renderer.sharedMaterials;
                string rendererPath = GetRendererPath(renderer);
                var result = new RendererSnapshot
                {
                    id = GetRendererId(renderer, rendererPath),
                    path = rendererPath,
                    gameObjectId = GetGlobalObjectId(renderer.gameObject),
                    rendererType = renderer.GetType().FullName,
                    activeInHierarchy = renderer.gameObject.activeInHierarchy,
                    enabled = renderer.enabled,
                    forceRenderingOff = renderer.forceRenderingOff,
                    meshId = GetObjectId(mesh),
                    meshName = mesh != null ? mesh.name : string.Empty,
                    meshVertexCount = mesh != null ? mesh.vertexCount : 0,
                    meshSubMeshCount = mesh != null ? mesh.subMeshCount : 0,
                    additionalVertexStreamsId = GetObjectId(additionalVertexStreams),
                    additionalVertexStreamsName =
                        additionalVertexStreams != null ? additionalVertexStreams.name : string.Empty,
                    localToWorldMatrix = MatrixToString(renderer.localToWorldMatrix),
                    lightmapIndex = renderer.lightmapIndex,
                    lightmapScaleOffset = VectorToString(renderer.lightmapScaleOffset),
                    realtimeLightmapIndex = renderer.realtimeLightmapIndex,
                    realtimeLightmapScaleOffset = VectorToString(renderer.realtimeLightmapScaleOffset),
                    lightProbeUsage = renderer.lightProbeUsage.ToString(),
                    reflectionProbeUsage = renderer.reflectionProbeUsage.ToString(),
                    probeAnchorId = GetObjectId(renderer.probeAnchor),
                    probeAnchorPath = GetTransformObjectPath(renderer.probeAnchor),
                    lightProbeProxyVolumeOverrideId =
                        GetObjectId(renderer.lightProbeProxyVolumeOverride),
                    lightProbeProxyVolumeOverridePath =
                        GetGameObjectObjectPath(renderer.lightProbeProxyVolumeOverride),
                    shadowCastingMode = renderer.shadowCastingMode.ToString(),
                    receiveShadows = renderer.receiveShadows,
                    motionVectorGenerationMode = renderer.motionVectorGenerationMode.ToString(),
                    allowOcclusionWhenDynamic = renderer.allowOcclusionWhenDynamic,
                    staticShadowCaster = renderer.staticShadowCaster,
                    rendererPriority = renderer.rendererPriority,
                    renderingLayerMask = renderer.renderingLayerMask,
                    sortingLayerId = renderer.sortingLayerID,
                    sortingLayerName = renderer.sortingLayerName,
                    sortingOrder = renderer.sortingOrder
                };

                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    result.materials.Add(CaptureMaterial(materials[materialIndex], materialIndex));

                List<ShaderPropertyDescriptor> unionProperties = GetShaderPropertyUnion(materials);
                result.rendererPropertyBlock = CapturePropertyBlock(
                    renderer, -1, "renderer", unionProperties);
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    result.materialPropertyBlocks.Add(CapturePropertyBlock(
                        renderer,
                        materialIndex,
                        "material-slot",
                        GetShaderProperties(materials[materialIndex] != null
                            ? materials[materialIndex].shader
                            : null)));
                }

                return result;
            }

            private static MaterialSnapshot CaptureMaterial(Material material, int slot)
            {
                var result = new MaterialSnapshot
                {
                    slot = slot,
                    isNull = material == null
                };
                if (material == null)
                    return result;

                Shader shader = material.shader;
                result.id = GetObjectId(material);
                result.name = material.name;
                result.assetPath = NormalizePath(AssetDatabase.GetAssetPath(material));
                result.shaderId = GetObjectId(shader);
                result.shaderName = shader != null ? shader.name : string.Empty;
                result.shaderAssetPath = NormalizePath(AssetDatabase.GetAssetPath(shader));
                result.renderQueue = material.renderQueue;
                result.rawRenderQueue = material.rawRenderQueue;
                result.shaderDefaultRenderQueue = shader != null ? shader.renderQueue : -1;
                result.renderTypeTag = material.GetTag("RenderType", false, string.Empty);
                result.queueTag = material.GetTag("Queue", false, string.Empty);
                result.renderPipelineTag = material.GetTag("RenderPipeline", false, string.Empty);
                result.enableInstancing = material.enableInstancing;
                result.doubleSidedGi = material.doubleSidedGI;
                result.globalIlluminationFlags = material.globalIlluminationFlags.ToString();
                result.keywords = (material.shaderKeywords ?? Array.Empty<string>())
                    .OrderBy(keyword => keyword, StringComparer.Ordinal)
                    .ToList();

                for (int passIndex = 0; passIndex < material.passCount; passIndex++)
                {
                    string passName = material.GetPassName(passIndex) ?? string.Empty;
                    result.passes.Add(new ShaderPassSnapshot
                    {
                        index = passIndex,
                        name = passName,
                        enabled = string.IsNullOrEmpty(passName) ||
                                  material.GetShaderPassEnabled(passName)
                    });
                }

                List<ShaderPropertyDescriptor> properties = GetShaderProperties(shader);
                for (int i = 0; i < properties.Count; i++)
                    result.properties.Add(CaptureMaterialProperty(material, properties[i]));
                return result;
            }

            private static ShaderPropertySnapshot CaptureMaterialProperty(
                Material material,
                ShaderPropertyDescriptor property)
            {
                var result = new ShaderPropertySnapshot
                {
                    index = property.index,
                    name = property.name,
                    description = property.description,
                    type = property.type.ToString(),
                    flags = property.flags
                };

                switch (property.type)
                {
                    case ShaderPropertyType.Color:
                        result.value = ColorToString(material.GetColor(property.id));
                        break;
                    case ShaderPropertyType.Vector:
                        result.value = VectorToString(material.GetVector(property.id));
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        result.value = FloatToString(material.GetFloat(property.id));
                        break;
                    case ShaderPropertyType.Int:
                        result.value = material.GetInteger(property.id).ToString(InvariantCulture);
                        break;
                    case ShaderPropertyType.Texture:
                        Texture texture = material.GetTexture(property.id);
                        result.textureId = GetObjectId(texture);
                        result.textureName = texture != null ? texture.name : string.Empty;
                        result.textureScale = Vector2ToString(material.GetTextureScale(property.id));
                        result.textureOffset = Vector2ToString(material.GetTextureOffset(property.id));
                        break;
                    default:
                        result.value = "<unsupported:" + property.type + ">";
                        break;
                }

                return result;
            }

            private static RendererPropertyBlockSnapshot CapturePropertyBlock(
                Renderer renderer,
                int materialIndex,
                string scope,
                IReadOnlyList<ShaderPropertyDescriptor> properties)
            {
                var block = new MaterialPropertyBlock();
                if (materialIndex < 0)
                    renderer.GetPropertyBlock(block);
                else
                    renderer.GetPropertyBlock(block, materialIndex);

                var result = new RendererPropertyBlockSnapshot
                {
                    scope = scope,
                    materialIndex = materialIndex,
                    isEmpty = block.isEmpty
                };

                for (int i = 0; i < properties.Count; i++)
                {
                    ShaderPropertyDescriptor property = properties[i];
                    var value = new PropertyBlockValueSnapshot
                    {
                        name = property.name,
                        type = property.type.ToString(),
                        hasOverride = block.HasProperty(property.id)
                    };

                    switch (property.type)
                    {
                        case ShaderPropertyType.Color:
                            value.value = ColorToString(block.GetColor(property.id));
                            break;
                        case ShaderPropertyType.Vector:
                            value.value = VectorToString(block.GetVector(property.id));
                            break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            value.value = FloatToString(block.GetFloat(property.id));
                            break;
                        case ShaderPropertyType.Int:
                            value.value = block.GetInteger(property.id).ToString(InvariantCulture);
                            break;
                        case ShaderPropertyType.Texture:
                            Texture texture = block.GetTexture(property.id);
                            value.textureId = GetObjectId(texture);
                            value.textureName = texture != null ? texture.name : string.Empty;
                            break;
                        default:
                            value.value = "<unsupported:" + property.type + ">";
                            break;
                    }

                    result.properties.Add(value);
                }

                return result;
            }

            private static List<ShaderPropertyDescriptor> GetShaderPropertyUnion(
                IReadOnlyList<Material> materials)
            {
                var byKey = new Dictionary<string, ShaderPropertyDescriptor>(StringComparer.Ordinal);
                for (int i = 0; i < materials.Count; i++)
                {
                    Material material = materials[i];
                    List<ShaderPropertyDescriptor> properties =
                        GetShaderProperties(material != null ? material.shader : null);
                    for (int propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
                    {
                        ShaderPropertyDescriptor property = properties[propertyIndex];
                        string key = property.name + "|" + property.type;
                        if (!byKey.ContainsKey(key))
                            byKey.Add(key, property);
                    }
                }

                return byKey.Values
                    .OrderBy(property => property.name, StringComparer.Ordinal)
                    .ThenBy(property => property.type.ToString(), StringComparer.Ordinal)
                    .ToList();
            }

            private static List<ShaderPropertyDescriptor> GetShaderProperties(Shader shader)
            {
                var result = new List<ShaderPropertyDescriptor>();
                if (shader == null)
                    return result;

                int propertyCount = shader.GetPropertyCount();
                for (int i = 0; i < propertyCount; i++)
                {
                    string propertyName = shader.GetPropertyName(i);
                    result.Add(new ShaderPropertyDescriptor
                    {
                        index = i,
                        id = Shader.PropertyToID(propertyName),
                        name = propertyName,
                        description = shader.GetPropertyDescription(i),
                        type = shader.GetPropertyType(i),
                        flags = shader.GetPropertyFlags(i).ToString()
                    });
                }

                return result;
            }

            private static List<DuplicateRendererGroup> FindDuplicateGroups(
                IReadOnlyList<RendererSnapshot> renderers)
            {
                return renderers
                    .Where(renderer => !string.IsNullOrEmpty(renderer.meshId))
                    .GroupBy(
                        renderer => renderer.meshId + "|" + renderer.localToWorldMatrix,
                        StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group =>
                    {
                        List<RendererSnapshot> members = group
                            .OrderBy(renderer => renderer.id, StringComparer.Ordinal)
                            .ToList();
                        return new DuplicateRendererGroup
                        {
                            signature = group.Key,
                            meshId = members[0].meshId,
                            localToWorldMatrix = members[0].localToWorldMatrix,
                            rendererIds = members.Select(renderer => renderer.id).ToList(),
                            rendererPaths = members.Select(renderer => renderer.path).ToList()
                        };
                    })
                    .OrderBy(group => group.signature, StringComparer.Ordinal)
                    .ToList();
            }

            private static Mesh GetRendererMesh(Renderer renderer)
            {
                if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
                    return skinnedMeshRenderer.sharedMesh;
                if (renderer is MeshRenderer)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    return filter != null ? filter.sharedMesh : null;
                }

                return null;
            }

            private static string GetRendererPath(Renderer renderer)
            {
                Renderer[] siblings = renderer.GetComponents<Renderer>();
                int rendererIndex = Array.IndexOf(siblings, renderer);
                return GetGameObjectPath(renderer.gameObject) +
                       $"::{renderer.GetType().FullName}[{rendererIndex}]";
            }

            private static string GetRendererId(Renderer renderer, string rendererPath)
            {
                string globalId = GetGlobalObjectId(renderer);
                return string.IsNullOrEmpty(globalId) ? "path:" + rendererPath : globalId;
            }

            private static string GetGlobalObjectId(UnityEngine.Object value)
            {
                if (value == null)
                    return string.Empty;
                GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(value);
                string text = globalId.ToString();
                return text.EndsWith("-0-0", StringComparison.Ordinal) ? string.Empty : text;
            }

            private static string GetObjectId(UnityEngine.Object value)
            {
                if (value == null)
                    return string.Empty;

                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        value, out string guid, out long localId) &&
                    !string.IsNullOrEmpty(guid))
                {
                    return $"asset:{guid}:{localId}";
                }

                string globalId = GetGlobalObjectId(value);
                if (!string.IsNullOrEmpty(globalId))
                    return globalId;

                if (value is Mesh mesh)
                {
                    return "runtime-mesh:" + Escape(mesh.name) +
                           $":vertices={mesh.vertexCount}:submeshes={mesh.subMeshCount}:" +
                           BoundsToString(mesh.bounds);
                }

                if (value is Material material)
                {
                    return "runtime-material:" + Escape(material.name) + ":shader=" +
                           GetObjectId(material.shader) + ":queue=" + material.renderQueue;
                }

                return "runtime-object:" + value.GetType().FullName + ":" + Escape(value.name);
            }

            private static string GetTransformObjectPath(Transform transform)
            {
                return transform != null ? GetGameObjectPath(transform.gameObject) : string.Empty;
            }

            private static string GetGameObjectObjectPath(GameObject gameObject)
            {
                return gameObject != null ? GetGameObjectPath(gameObject) : string.Empty;
            }

            private static string GetTransformPath(Transform transform)
            {
                var segments = new List<string>();
                Transform current = transform;
                while (current != null)
                {
                    segments.Add(Escape(current.name) + "[" + current.GetSiblingIndex() + "]");
                    current = current.parent;
                }

                segments.Reverse();
                return string.Join("/", segments);
            }

            private static string NormalizePath(string path)
            {
                return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
            }

            private static string Escape(string value)
            {
                return (value ?? string.Empty)
                    .Replace("\\", "\\\\")
                    .Replace("/", "\\/")
                    .Replace(":", "\\:");
            }

            private static string FloatToString(float value)
            {
                return value.ToString("R", InvariantCulture);
            }

            private static string Vector2ToString(Vector2 value)
            {
                return FloatToString(value.x) + "," + FloatToString(value.y);
            }

            private static string VectorToString(Vector4 value)
            {
                return FloatToString(value.x) + "," + FloatToString(value.y) + "," +
                       FloatToString(value.z) + "," + FloatToString(value.w);
            }

            private static string ColorToString(Color value)
            {
                return FloatToString(value.r) + "," + FloatToString(value.g) + "," +
                       FloatToString(value.b) + "," + FloatToString(value.a);
            }

            private static string MatrixToString(Matrix4x4 value)
            {
                var builder = new StringBuilder(16 * 12);
                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 4; column++)
                    {
                        if (builder.Length > 0)
                            builder.Append(',');
                        builder.Append(FloatToString(value[row, column]));
                    }
                }

                return builder.ToString();
            }

            private static string BoundsToString(Bounds bounds)
            {
                return "center=" + VectorToString(bounds.center) +
                       ":extents=" + VectorToString(bounds.extents);
            }

            private sealed class ShaderPropertyDescriptor
            {
                public int index;
                public int id;
                public string name;
                public string description;
                public ShaderPropertyType type;
                public string flags;
            }
        }

        internal static class RendererParityComparer
        {
            public static RendererParityComparison Compare(
                RendererParitySnapshot baseline,
                RendererParitySnapshot current)
            {
                var result = new RendererParityComparison
                {
                    baseline = baseline,
                    current = current
                };

                Dictionary<string, RendererSnapshot> baselineById = baseline.renderers
                    .ToDictionary(renderer => renderer.id, StringComparer.Ordinal);
                Dictionary<string, RendererSnapshot> currentById = current.renderers
                    .ToDictionary(renderer => renderer.id, StringComparer.Ordinal);

                foreach (KeyValuePair<string, RendererSnapshot> pair in baselineById
                             .OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    if (!currentById.TryGetValue(pair.Key, out RendererSnapshot currentRenderer))
                    {
                        result.removed.Add(pair.Value);
                        continue;
                    }

                    List<string> changedSections = GetChangedSections(pair.Value, currentRenderer);
                    if (changedSections.Count == 0)
                    {
                        result.unchangedCount++;
                        continue;
                    }

                    result.changed.Add(new RendererChangeSnapshot
                    {
                        id = pair.Key,
                        path = currentRenderer.path,
                        changedSections = changedSections,
                        baseline = pair.Value,
                        current = currentRenderer
                    });
                }

                foreach (KeyValuePair<string, RendererSnapshot> pair in currentById
                             .OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    if (!baselineById.ContainsKey(pair.Key))
                        result.added.Add(pair.Value);
                }

                Dictionary<string, DuplicateRendererGroup> baselineDuplicates =
                    baseline.duplicateGroups.ToDictionary(
                        group => group.signature, StringComparer.Ordinal);
                Dictionary<string, DuplicateRendererGroup> currentDuplicates =
                    current.duplicateGroups.ToDictionary(
                        group => group.signature, StringComparer.Ordinal);

                foreach (DuplicateRendererGroup currentGroup in current.duplicateGroups)
                {
                    if (!baselineDuplicates.TryGetValue(
                            currentGroup.signature, out DuplicateRendererGroup baselineGroup) ||
                        currentGroup.rendererIds.Except(
                            baselineGroup.rendererIds, StringComparer.Ordinal).Any())
                    {
                        result.newDuplicateGroups.Add(currentGroup);
                    }
                }

                foreach (DuplicateRendererGroup baselineGroup in baseline.duplicateGroups)
                {
                    if (!currentDuplicates.TryGetValue(
                            baselineGroup.signature, out DuplicateRendererGroup currentGroup) ||
                        baselineGroup.rendererIds.Except(
                            currentGroup.rendererIds, StringComparer.Ordinal).Any())
                    {
                        result.resolvedDuplicateGroups.Add(baselineGroup);
                    }
                }

                result.HasParityFailure = result.changed.Count > 0 || result.added.Count > 0 ||
                                          result.removed.Count > 0 ||
                                          result.newDuplicateGroups.Count > 0;
                return result;
            }

            private static List<string> GetChangedSections(
                RendererSnapshot baseline,
                RendererSnapshot current)
            {
                var changed = new List<string>();
                AddIfDifferent(changed, "identity/path/type",
                    Canonical(baseline.path, baseline.gameObjectId, baseline.rendererType,
                        baseline.activeInHierarchy),
                    Canonical(current.path, current.gameObjectId, current.rendererType,
                        current.activeInHierarchy));
                AddIfDifferent(changed, "enabled/forceRenderingOff",
                    Canonical(baseline.enabled, baseline.forceRenderingOff),
                    Canonical(current.enabled, current.forceRenderingOff));
                AddIfDifferent(changed, "mesh/additionalVertexStreams",
                    Canonical(baseline.meshId, baseline.meshName, baseline.meshVertexCount,
                        baseline.meshSubMeshCount, baseline.additionalVertexStreamsId,
                        baseline.additionalVertexStreamsName),
                    Canonical(current.meshId, current.meshName, current.meshVertexCount,
                        current.meshSubMeshCount, current.additionalVertexStreamsId,
                        current.additionalVertexStreamsName));
                AddIfDifferent(changed, "worldTransform",
                    baseline.localToWorldMatrix, current.localToWorldMatrix);
                AddIfDifferent(changed, "materials/shaders/keywords/queues/properties/passes",
                    JsonUtility.ToJson(new MaterialListBox { value = baseline.materials }),
                    JsonUtility.ToJson(new MaterialListBox { value = current.materials }));
                AddIfDifferent(changed, "lightmaps",
                    Canonical(baseline.lightmapIndex, baseline.lightmapScaleOffset,
                        baseline.realtimeLightmapIndex, baseline.realtimeLightmapScaleOffset),
                    Canonical(current.lightmapIndex, current.lightmapScaleOffset,
                        current.realtimeLightmapIndex, current.realtimeLightmapScaleOffset));
                AddIfDifferent(changed, "probes",
                    Canonical(baseline.lightProbeUsage, baseline.reflectionProbeUsage,
                        baseline.probeAnchorId, baseline.probeAnchorPath,
                        baseline.lightProbeProxyVolumeOverrideId,
                        baseline.lightProbeProxyVolumeOverridePath),
                    Canonical(current.lightProbeUsage, current.reflectionProbeUsage,
                        current.probeAnchorId, current.probeAnchorPath,
                        current.lightProbeProxyVolumeOverrideId,
                        current.lightProbeProxyVolumeOverridePath));
                AddIfDifferent(changed, "shadows",
                    Canonical(baseline.shadowCastingMode, baseline.receiveShadows,
                        baseline.staticShadowCaster),
                    Canonical(current.shadowCastingMode, current.receiveShadows,
                        current.staticShadowCaster));
                AddIfDifferent(changed, "rendererFlags",
                    Canonical(baseline.motionVectorGenerationMode,
                        baseline.allowOcclusionWhenDynamic, baseline.rendererPriority,
                        baseline.renderingLayerMask, baseline.sortingLayerId,
                        baseline.sortingLayerName, baseline.sortingOrder),
                    Canonical(current.motionVectorGenerationMode,
                        current.allowOcclusionWhenDynamic, current.rendererPriority,
                        current.renderingLayerMask, current.sortingLayerId,
                        current.sortingLayerName, current.sortingOrder));
                AddIfDifferent(changed, "rendererMaterialPropertyBlock",
                    JsonUtility.ToJson(baseline.rendererPropertyBlock),
                    JsonUtility.ToJson(current.rendererPropertyBlock));
                AddIfDifferent(changed, "perMaterialPropertyBlocks",
                    JsonUtility.ToJson(new PropertyBlockListBox
                        { value = baseline.materialPropertyBlocks }),
                    JsonUtility.ToJson(new PropertyBlockListBox
                        { value = current.materialPropertyBlocks }));
                return changed;
            }

            private static void AddIfDifferent(
                ICollection<string> changed,
                string section,
                string baseline,
                string current)
            {
                if (!string.Equals(baseline, current, StringComparison.Ordinal))
                    changed.Add(section);
            }

            private static string Canonical(params object[] values)
            {
                var builder = new StringBuilder();
                for (int i = 0; i < values.Length; i++)
                {
                    string text;
                    if (values[i] == null)
                    {
                        text = "<null>";
                    }
                    else if (values[i] is IFormattable formattable)
                    {
                        text = formattable.ToString(null, CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        text = values[i].ToString();
                    }

                    builder.Append(text.Length)
                        .Append(':')
                        .Append(text)
                        .Append(';');
                }

                return builder.ToString();
            }

            [Serializable]
            private sealed class MaterialListBox
            {
                public List<MaterialSnapshot> value;
            }

            [Serializable]
            private sealed class PropertyBlockListBox
            {
                public List<RendererPropertyBlockSnapshot> value;
            }
        }

        internal static class RendererParityReportWriter
        {
            public static void WriteBaseline(RendererParitySnapshot baseline)
            {
                WriteReport(BaselineJsonAssetPath, JsonUtility.ToJson(baseline, true));
                WriteReport(BaselineTextAssetPath, BuildBaselineText(baseline));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            public static RendererParitySnapshot ReadBaseline()
            {
                string absolutePath = ToAbsolutePath(BaselineJsonAssetPath);
                if (!File.Exists(absolutePath))
                    throw new FileNotFoundException("Renderer parity baseline is missing.", absolutePath);

                RendererParitySnapshot baseline =
                    JsonUtility.FromJson<RendererParitySnapshot>(File.ReadAllText(absolutePath));
                if (baseline == null || baseline.formatVersion != 1)
                    throw new InvalidDataException("Unsupported or invalid renderer parity baseline.");
                return baseline;
            }

            public static void WriteComparison(RendererParityComparison comparison)
            {
                WriteReport(ComparisonJsonAssetPath, JsonUtility.ToJson(comparison, true));
                WriteReport(ComparisonTextAssetPath, BuildComparisonText(comparison));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            private static void WriteReport(string assetPath, string content)
            {
                string absolutePath = ToAbsolutePath(assetPath);
                string directory = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(absolutePath, NormalizeNewlines(content), new UTF8Encoding(false));
            }

            private static string BuildBaselineText(RendererParitySnapshot baseline)
            {
                var builder = new StringBuilder();
                builder.AppendLine("PORTAL RENDERER PARITY BASELINE");
                builder.AppendLine("formatVersion=" + baseline.formatVersion);
                builder.AppendLine("unityVersion=" + baseline.unityVersion);
                builder.AppendLine("roots=" + baseline.roots.Count);
                builder.AppendLine("renderers=" + baseline.renderers.Count);
                builder.AppendLine("duplicateGroups=" + baseline.duplicateGroups.Count);
                builder.AppendLine();
                builder.AppendLine("ROOTS");
                for (int i = 0; i < baseline.roots.Count; i++)
                    builder.AppendLine(baseline.roots[i].path + " | " + baseline.roots[i].globalObjectId);
                builder.AppendLine();
                builder.AppendLine("RENDERERS");
                for (int i = 0; i < baseline.renderers.Count; i++)
                {
                    RendererSnapshot renderer = baseline.renderers[i];
                    builder.AppendLine(
                        renderer.path + " | id=" + renderer.id + " | mesh=" + renderer.meshId +
                        " | materials=" + renderer.materials.Count + " | enabled=" + renderer.enabled);
                }
                AppendDuplicateGroups(builder, "DUPLICATE MESH+WORLD-TRANSFORM GROUPS",
                    baseline.duplicateGroups);
                return builder.ToString();
            }

            private static string BuildComparisonText(RendererParityComparison comparison)
            {
                var builder = new StringBuilder();
                builder.AppendLine(comparison.HasParityFailure
                    ? "FAIL PORTAL RENDERER PARITY"
                    : "PASS PORTAL RENDERER PARITY");
                builder.AppendLine("unchanged=" + comparison.unchangedCount);
                builder.AppendLine("changed=" + comparison.changed.Count);
                builder.AppendLine("added=" + comparison.added.Count);
                builder.AppendLine("removed=" + comparison.removed.Count);
                builder.AppendLine("newDuplicateGroups=" + comparison.newDuplicateGroups.Count);
                builder.AppendLine("resolvedDuplicateGroups=" + comparison.resolvedDuplicateGroups.Count);

                builder.AppendLine();
                builder.AppendLine("CHANGED");
                for (int i = 0; i < comparison.changed.Count; i++)
                {
                    RendererChangeSnapshot change = comparison.changed[i];
                    builder.AppendLine(change.path + " | " + string.Join(", ", change.changedSections));
                }

                builder.AppendLine();
                builder.AppendLine("ADDED");
                for (int i = 0; i < comparison.added.Count; i++)
                    builder.AppendLine(comparison.added[i].path + " | " + comparison.added[i].id);

                builder.AppendLine();
                builder.AppendLine("REMOVED");
                for (int i = 0; i < comparison.removed.Count; i++)
                    builder.AppendLine(comparison.removed[i].path + " | " + comparison.removed[i].id);

                AppendDuplicateGroups(builder, "NEW DUPLICATE MESH+WORLD-TRANSFORM GROUPS",
                    comparison.newDuplicateGroups);
                AppendDuplicateGroups(builder, "RESOLVED DUPLICATE MESH+WORLD-TRANSFORM GROUPS",
                    comparison.resolvedDuplicateGroups);
                return builder.ToString();
            }

            private static void AppendDuplicateGroups(
                StringBuilder builder,
                string heading,
                IReadOnlyList<DuplicateRendererGroup> groups)
            {
                builder.AppendLine();
                builder.AppendLine(heading);
                for (int i = 0; i < groups.Count; i++)
                {
                    DuplicateRendererGroup group = groups[i];
                    builder.AppendLine("mesh=" + group.meshId);
                    for (int memberIndex = 0; memberIndex < group.rendererPaths.Count; memberIndex++)
                        builder.AppendLine("  " + group.rendererPaths[memberIndex]);
                }
            }

            private static string NormalizeNewlines(string value)
            {
                return (value ?? string.Empty)
                    .Replace("\r\n", "\n")
                    .Replace("\r", "\n");
            }

            private static string ToAbsolutePath(string assetPath)
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            }
        }
    }
}
