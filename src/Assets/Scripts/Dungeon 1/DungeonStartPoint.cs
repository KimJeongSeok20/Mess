using UnityEngine;

/// <summary>
/// Spawn/teleport anchor inside the always-lit StartRoom tile. Also turns the reception window
/// wall ("Wall_Interior_Reception") into the revive counter at runtime, so every generated
/// dungeon has one without editing the room prefab. Dead players wait in the booth behind it.
/// </summary>
public class DungeonStartPoint : MonoBehaviour
{
    public static Transform Instance { get; private set; }

    [Header("Revive Station (runtime)")]
    [SerializeField] private bool createReviveStation = true;
    [SerializeField] private string receptionWallName = "Wall_Interior_Reception";
    [Tooltip("창문 벽에서 부스 안쪽으로 얼마나 들어간 곳에 유령을 세울지")]
    [SerializeField, Min(0.3f)] private float boothDepth = 1.6f;
    [Tooltip("창문 벽 바깥(메인 룸 쪽)으로 얼마나 떨어진 곳에서 부활할지")]
    [SerializeField, Min(0.3f)] private float reviveDistance = 1.4f;
    [SerializeField] private Vector3 fallbackStationLocalOffset = new Vector3(2.2f, 0f, 1.2f);
    [SerializeField] private Color stationColor = new Color(0.2f, 1f, 0.55f);

    private void Awake()
    {
        Instance = transform;

        if (createReviveStation && FindFirstObjectByType<ReviveStation>() == null)
        {
            if (!TryAttachToReceptionWall())
                BuildFallbackStation();
        }
    }

    private void OnDestroy()
    {
        if (Instance == transform)
            Instance = null;
    }

    private Transform FindTileRoot()
    {
        var tile = GetComponentInParent<DunGen.Tile>();
        if (tile != null)
            return tile.transform;

        return transform.parent != null ? transform.parent : transform;
    }

    private bool TryAttachToReceptionWall()
    {
        Transform tileRoot = FindTileRoot();
        Transform wall = null;
        foreach (Transform child in tileRoot.GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child.name == receptionWallName)
            {
                wall = child;
                break;
            }
        }

        if (wall == null)
            return false;

        // Bounds of the window wall in world space → a box collider the player can look at.
        Renderer[] renderers = wall.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return false;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        int interactableLayer = LayerMask.NameToLayer("Interactable");

        var counter = new GameObject("ReviveCounter");
        counter.transform.SetParent(wall, false);
        counter.transform.position = bounds.center;
        counter.transform.rotation = wall.rotation;
        if (interactableLayer >= 0)
            counter.layer = interactableLayer;

        var box = counter.AddComponent<BoxCollider>();
        Vector3 localSize = counter.transform.InverseTransformVector(bounds.size);
        box.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Max(0.25f, Mathf.Abs(localSize.z)));
        box.center = Vector3.zero;

        // Which side is the booth? The side facing away from the start point (main room).
        Vector3 toStart = transform.position - bounds.center;
        toStart.y = 0f;
        Vector3 wallNormal = wall.forward;
        wallNormal.y = 0f;
        if (wallNormal.sqrMagnitude < 0.001f)
            wallNormal = Vector3.forward;
        wallNormal.Normalize();
        if (Vector3.Dot(wallNormal, toStart) < 0f)
            wallNormal = -wallNormal; // now points toward the main room

        Vector3 floorY = new Vector3(0f, transform.position.y - bounds.center.y, 0f);
        Vector3 reviveWorld = bounds.center + wallNormal * reviveDistance + floorY;
        Vector3 waitingWorld = bounds.center - wallNormal * boothDepth + floorY;

        var station = counter.AddComponent<ReviveStation>();
        station.HoverOutlineRoot = wall;
        station.Configure(
            counter.transform.InverseTransformPoint(waitingWorld),
            counter.transform.InverseTransformPoint(reviveWorld));

        var glow = new GameObject("Glow");
        glow.transform.SetParent(counter.transform, false);
        glow.transform.position = bounds.center + wallNormal * 0.6f;
        var light = glow.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = stationColor;
        light.intensity = 1.8f;
        light.range = 4f;

        Debug.Log($"[DungeonStartPoint] Revive counter attached to '{wall.name}'. waiting={waitingWorld} revive={reviveWorld}", this);
        return true;
    }

    private void BuildFallbackStation()
    {
        int interactableLayer = LayerMask.NameToLayer("Interactable");

        var root = new GameObject("ReviveStation");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = fallbackStationLocalOffset;
        root.transform.localRotation = Quaternion.identity;
        if (interactableLayer >= 0)
            root.layer = interactableLayer;

        GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.name = "Pillar";
        pillar.transform.SetParent(root.transform, false);
        pillar.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        pillar.transform.localScale = new Vector3(0.45f, 0.6f, 0.45f);
        if (interactableLayer >= 0)
            pillar.layer = interactableLayer;

        var renderer = pillar.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader != null)
            {
                var material = new Material(shader) { name = "ReviveStation_Runtime" };
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", stationColor * 0.35f);
                if (material.HasProperty("_Color")) material.SetColor("_Color", stationColor * 0.35f);
                material.EnableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", stationColor * 2.5f);
                renderer.material = material;
            }
        }

        var station = root.AddComponent<ReviveStation>();
        station.Configure(new Vector3(0f, 0f, -1.5f), new Vector3(0f, 0f, 1.5f));
        Debug.LogWarning($"[DungeonStartPoint] '{receptionWallName}' not found; using a fallback revive pillar.", this);
    }
}
