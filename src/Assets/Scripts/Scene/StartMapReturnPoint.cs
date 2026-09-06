using Demo.Scripts.Runtime.Character;
using UnityEngine;

public class StartMapReturnPoint : MonoBehaviour
{
    public static Transform Instance { get; private set; }

    private void Awake()
    {
        Instance = transform;
    }

    /// <summary>
    /// If the local player is still inside the dungeon, teleport them back to camp and restore
    /// the outdoor rendering/audio state. Used when the day ends or the run restarts, because the
    /// dungeon geometry is destroyed right after and the player would otherwise fall forever.
    /// </summary>
    public static bool ReturnLocalPlayerFromDungeon(string reason)
    {
        if (Instance == null)
            return false;

        DungeonZoneManager zone = FindFirstObjectByType<DungeonZoneManager>();
        if (zone == null || !zone.IsInDungeon)
            return false;

        Transform playerRoot = null;
        if (FPSMovement.LocalPlayerPosition != null)
            playerRoot = FPSMovement.LocalPlayerPosition.transform.root;
        else if (Camera.main != null)
            playerRoot = Camera.main.transform.root;

        if (playerRoot == null)
            return false;

        var cc = playerRoot.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        playerRoot.SetPositionAndRotation(Instance.position, Instance.rotation);
        Physics.SyncTransforms();

        if (cc != null) cc.enabled = true;

        zone.ExitDungeon(playerRoot);

        var fpsController = playerRoot.GetComponentInChildren<FPSController>();
        if (fpsController != null)
        {
            fpsController.SetDungeonPostProcessing(false);
            if (fpsController.isSpawned)
                fpsController.SetDungeonRenderLayerServerRpc(false);
        }

        Debug.Log($"[StartMapReturnPoint] Returned local player from the dungeon ({reason}).");
        return true;
    }
}
