using UnityEngine;

public class Billboard : MonoBehaviour
{
    private void LateUpdate()
    {
        if (Camera.main) transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position);
    }
}
