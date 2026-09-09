using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class BloodPoolVisual : MonoBehaviour
{
    private Mesh _runtimeMesh;

    public static GameObject SpawnOnGround(
        Vector3 source,
        Transform ignoredRoot,
        float scale,
        GameObject prefab,
        float lifetime)
    {
        if (prefab == null)
            return null;

        RaycastHit floorHit = Physics.RaycastAll(
                source + Vector3.up * 0.25f,
                Vector3.down,
                8f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore)
            .Where(hit => hit.collider != null
                && hit.normal.y > 0.35f
                && (ignoredRoot == null || !hit.collider.transform.IsChildOf(ignoredRoot)))
            .OrderBy(hit => hit.distance)
            .FirstOrDefault();
        if (floorHit.collider == null)
            return null;

        return SpawnAtGround(floorHit.point, floorHit.normal, scale, prefab, lifetime);
    }

    public static GameObject SpawnAtGround(Vector3 position, Vector3 normal,
        float scale, GameObject prefab, float lifetime)
    {
        if (prefab == null) return null;
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal)
            * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
            * prefab.transform.rotation;
        GameObject pool = Instantiate(prefab, position + normal * 0.012f, rotation);
        pool.transform.localScale = prefab.transform.localScale * Mathf.Max(0.1f, scale);
        pool.AddComponent<BloodPoolVisual>();
        Destroy(pool, Mathf.Max(0.1f, lifetime));
        return pool;
    }

    public static GameObject SpawnOnGround(
        Vector3 source,
        Transform ignoredRoot,
        float scale,
        Material material,
        Color color,
        float lifetime)
    {
        if (material == null)
            return null;

        RaycastHit floorHit = Physics.RaycastAll(
                source + Vector3.up * 2f,
                Vector3.down,
                8f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore)
            .Where(hit => hit.collider != null
                && (ignoredRoot == null || !hit.collider.transform.IsChildOf(ignoredRoot)))
            .OrderBy(hit => hit.distance)
            .FirstOrDefault();
        if (floorHit.collider == null)
            return null;

        var pool = new GameObject("BloodPool");
        pool.transform.SetPositionAndRotation(
            floorHit.point + floorHit.normal * 0.012f,
            Quaternion.FromToRotation(Vector3.up, floorHit.normal));

        var visual = pool.AddComponent<BloodPoolVisual>();
        var filter = pool.AddComponent<MeshFilter>();
        var renderer = pool.AddComponent<MeshRenderer>();
        visual._runtimeMesh = BuildMesh(Mathf.Max(0.1f, scale));
        filter.sharedMesh = visual._runtimeMesh;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        var properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", color);
        properties.SetColor("_Color", color);
        renderer.SetPropertyBlock(properties);

        if (lifetime > 0f)
            Destroy(pool, lifetime);
        return pool;
    }

    private static Mesh BuildMesh(float scale)
    {
        const int segments = 20;
        var vertices = new Vector3[segments + 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[segments * 3];
        vertices[0] = Vector3.zero;
        uv[0] = new Vector2(0.5f, 0.5f);

        float baseRadius = 0.9f * scale;
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            float irregularity = 0.76f
                + Mathf.Sin(angle * 3f + 0.4f) * 0.16f
                + Mathf.Sin(angle * 7f + 1.3f) * 0.08f;
            float radius = baseRadius * irregularity;
            vertices[i + 1] = new Vector3(
                Mathf.Cos(angle) * radius,
                0f,
                Mathf.Sin(angle) * radius);
            uv[i + 1] = new Vector2(
                0.5f + Mathf.Cos(angle) * 0.5f,
                0.5f + Mathf.Sin(angle) * 0.5f);

            if (i < segments)
            {
                int triangle = i * 3;
                triangles[triangle] = 0;
                triangles[triangle + 1] = i + 2;
                triangles[triangle + 2] = i + 1;
            }
        }

        var mesh = new Mesh { name = "BloodPool_RuntimeMesh" };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDestroy()
    {
        if (_runtimeMesh != null)
            Destroy(_runtimeMesh);
    }
}
