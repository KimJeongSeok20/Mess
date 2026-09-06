using UnityEngine;
using UnityEditor;

public class RemoveCeilSpawnPoints
{
    [MenuItem("Tools/Delete ItemSpawnPoints Under Ceil")]
    private static void DeleteSpawnPoints()
    {
        GameObject ceil = GameObject.Find("Ceil");
        if (ceil == null)
        {
            Debug.LogWarning("Ceil object not found.");
            return;
        }

        var spawnPoints = ceil.GetComponentsInChildren<Transform>(true);

        int count = 0;
        foreach (var t in spawnPoints)
        {
            if (t.name == "ItemSpawnPoint")
            {
                GameObject.DestroyImmediate(t.gameObject);
                count++;
            }
        }

        Debug.Log($"Deleted {count} ItemSpawnPoint under Ceil.");
    }
}
