using UnityEngine;

[RequireComponent(typeof(Transform))]
public class WorldHealthBar : MonoBehaviour
{
    private GameObject back;
    private GameObject front;
    private float max = 100f;

    public void SetupFallback()
    {
        // Background
        back = GameObject.CreatePrimitive(PrimitiveType.Quad);
        back.transform.SetParent(transform, false);
        back.transform.localScale = new Vector3(0.6f, 0.08f, 1f);
        back.transform.localPosition = Vector3.zero;
        var backR = back.GetComponent<Renderer>();
        backR.material = new Material(Shader.Find("Unlit/Color"));
        backR.material.color = Color.red;
        DestroyImmediate(back.GetComponent<Collider>());

        // Foreground
        front = GameObject.CreatePrimitive(PrimitiveType.Quad);
        front.transform.SetParent(transform, false);
        front.transform.localScale = new Vector3(0.6f, 0.08f, 1f);
        front.transform.localPosition = new Vector3(-0.3f + 0.3f, 0f, -0.01f);
        var frontR = front.GetComponent<Renderer>();
        frontR.material = new Material(Shader.Find("Unlit/Color"));
        frontR.material.color = Color.green;
        DestroyImmediate(front.GetComponent<Collider>());

        // Make the bar always face camera by adding a simple updater
        var b = gameObject.AddComponent<Billboard>();
        b.enabled = true;
    }

    public void SetMax(int m)
    {
        max = Mathf.Max(1, m);
    }

    public void Set(int cur)
    {
        float t = Mathf.Clamp01(cur / max);
        if (front != null)
        {
            front.transform.localScale = new Vector3(0.6f * t, 0.08f, 1f);
            front.transform.localPosition = new Vector3(-0.3f + 0.6f * t * 0.5f, 0f, -0.01f);
        }
    }
}
