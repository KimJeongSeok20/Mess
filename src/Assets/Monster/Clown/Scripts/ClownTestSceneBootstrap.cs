using UnityEngine;

public class ClownTestSceneBootstrap : MonoBehaviour
{
    [SerializeField] private Transform registryRoot;

    private void Start()
    {
        if (registryRoot != null)
        {
            var method = typeof(global::DungeonPointRegistry).GetMethod("RebuildFromRoot", new[] { typeof(Transform) });
            method?.Invoke(null, new object[] { registryRoot });
        }
    }
}
