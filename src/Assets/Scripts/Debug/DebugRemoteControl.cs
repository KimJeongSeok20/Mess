// DebugRemoteControl.cs - CLI-controllable player bridge for unity-cli exec
//
// Auto-creates a DontDestroyOnLoad singleton on play.
// All public static methods return string for unity-cli output.
//
// Usage examples:
//   unity-cli exec "return DebugRemoteControl.Forward();"
//   unity-cli exec "return DebugRemoteControl.Fire();"
//   unity-cli exec "return DebugRemoteControl.Status();"
//   unity-cli exec "return DebugRemoteControl.Help();"

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine.AI;
using DunGen;
using Demo.Scripts.Runtime.Character;
using KINEMATION.Shared.KAnimationCore.Runtime.Input;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using OccaSoftware.Altos.Runtime;
using PurrNet;

[DefaultExecutionOrder(-50)] // Run after input events, before FPSMovement.Update()
public partial class DebugRemoteControl : MonoBehaviour
{
    // ==================== Singleton ====================
    private static DebugRemoteControl _instance;

    // ==================== Movement Override State ====================
    private Vector2 _moveOverride;
    private bool _moveActive;
    private float _moveExpiry;
    private bool _sprintOverrideActive;

    // ==================== Cached Reflection ====================
    private static FieldInfo _inputDirectionField;
    private static FieldInfo _aimStateField;
    private static PropertyInfo _movementStateProp;
    private string _clownGiftImpactTestResult;
    private string _dungeonReadyResult;

    // ==================== Auto-Init ====================
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInit()
    {
        if (_instance != null) return;

        var go = new GameObject("[DebugRemoteControl]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<DebugRemoteControl>();

        // Cache reflection (one-time cost)
        _inputDirectionField = typeof(FPSMovement).GetField(
            "_inputDirection", BindingFlags.NonPublic | BindingFlags.Instance);

        _aimStateField = typeof(FPSController).GetField(
            "_aimState", BindingFlags.NonPublic | BindingFlags.Instance);

        _movementStateProp = typeof(FPSMovement).GetProperty(
            "MovementState", BindingFlags.Public | BindingFlags.Instance);

        Debug.Log("[DebugRemoteControl] Ready. unity-cli exec \"return DebugRemoteControl.Help();\"");
    }

    private static void EnsureInstance()
    {
        if (_instance == null) AutoInit();
    }

    // ==================== Player Finding ====================

    private static FPSController FindLocalController()
    {
        var cached = FPSController._fpsconnect;
        if (cached != null && cached.isOwner) return cached;

        // Fallback: scan all controllers
        var all = FindObjectsByType<FPSController>(FindObjectsSortMode.None);
        foreach (var c in all)
        {
            if (c.isOwner) return c;
        }
        return null;
    }

    private static FPSMovement FindMovement()
    {
        var ctrl = FindLocalController();
        return ctrl != null ? ctrl.GetComponent<FPSMovement>() : null;
    }

    private static FPSInputHandler FindInputHandler()
    {
        var ctrl = FindLocalController();
        return ctrl != null ? ctrl.GetComponent<FPSInputHandler>() : null;
    }

    private static FPSLookController FindLookController()
    {
        var ctrl = FindLocalController();
        return ctrl != null ? ctrl.GetComponent<FPSLookController>() : null;
    }

    private static Transform FindLocalPlayerRoot()
    {
        var ctrl = FindLocalController();
        return ctrl != null ? ctrl.transform.root : null;
    }

    private static NetworkDungeonController FindDungeonController()
    {
        return Object.FindFirstObjectByType<NetworkDungeonController>();
    }

    private static DungeonZoneManager FindDungeonZoneManager()
    {
        return Object.FindFirstObjectByType<DungeonZoneManager>();
    }

    private static DungeonMonsterSpawner FindMonsterSpawner()
    {
        var controller = FindDungeonController();
        return controller != null ? controller.GetComponent<DungeonMonsterSpawner>() : null;
    }

    private static DungeonLootPostProcessor FindLootPostProcessor()
    {
        var controller = FindDungeonController();
        return controller != null ? controller.GetComponent<DungeonLootPostProcessor>() : null;
    }

    private static bool TryTeleportPlayer(Vector3 position, Quaternion rotation, bool applyRotation)
    {
        var playerRoot = FindLocalPlayerRoot();
        if (playerRoot == null)
            return false;

        var cc = playerRoot.GetComponent<CharacterController>();
        if (cc != null)
            cc.enabled = false;

        playerRoot.position = position;
        if (applyRotation)
            playerRoot.rotation = rotation;

        if (cc != null)
            cc.enabled = true;

        return true;
    }

    private static bool TryResolveRandomDungeonDestination(float navmeshRadius, out Transform sourcePoint, out Vector3 destination, out string sourceLabel)
    {
        sourcePoint = null;
        destination = default;
        sourceLabel = null;

        var candidates = new List<Transform>(DungeonPointRegistry.SpawnPoints.Count + DungeonPointRegistry.PatrolPoints.Count);

        for (int i = 0; i < DungeonPointRegistry.SpawnPoints.Count; i++)
        {
            var point = DungeonPointRegistry.SpawnPoints[i];
            if (point != null)
                candidates.Add(point);
        }

        for (int i = 0; i < DungeonPointRegistry.PatrolPoints.Count; i++)
        {
            var point = DungeonPointRegistry.PatrolPoints[i];
            if (point != null)
                candidates.Add(point);
        }

        if (candidates.Count == 0 && DungeonStartPoint.Instance != null)
            candidates.Add(DungeonStartPoint.Instance);

        if (candidates.Count == 0)
            return false;

        int tries = Mathf.Max(8, candidates.Count);
        for (int i = 0; i < tries; i++)
        {
            var candidate = candidates[Random.Range(0, candidates.Count)];
            if (candidate == null)
                continue;

            if (NavMesh.SamplePosition(candidate.position, out var hit, navmeshRadius, NavMesh.AllAreas))
            {
                sourcePoint = candidate;
                destination = hit.position;
                sourceLabel = DungeonPointRegistry.SpawnPoints.Contains(candidate)
                    ? $"Spawn point '{candidate.name}'"
                    : $"Patrol point '{candidate.name}'";
                return true;
            }
        }

        var runtimeDungeon = Object.FindFirstObjectByType<RuntimeDungeon>();
        var tiles = runtimeDungeon != null ? runtimeDungeon.Generator.CurrentDungeon?.AllTiles : null;
        if (tiles == null || tiles.Count == 0)
            return false;

        int tileTries = Mathf.Max(8, tiles.Count * 2);
        for (int i = 0; i < tileTries; i++)
        {
            var tile = tiles[Random.Range(0, tiles.Count)];
            if (tile == null)
                continue;

            Bounds bounds = tile.Bounds;
            Vector3 sample = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                bounds.max.y + 1f,
                Random.Range(bounds.min.z, bounds.max.z));

            if (!NavMesh.SamplePosition(sample, out var hit, navmeshRadius, NavMesh.AllAreas))
                continue;

            destination = hit.position;
            sourceLabel = $"tile '{tile.name}'";
            return true;
        }

        return false;
    }

    // ==================== Frame Loop ====================

    private void Update()
    {
        if (!_moveActive) return;

        // Auto-expire movement
        if (Time.time > _moveExpiry)
        {
            _moveActive = false;
            _sprintOverrideActive = false;
            SetInputDirection(Vector2.zero);
            return;
        }

        // Override _inputDirection every frame (after input events, before FPSMovement)
        SetInputDirection(_moveOverride);

        if (_sprintOverrideActive)
        {
            var movement = FindMovement();
            if (movement != null && _movementStateProp != null)
            {
                var setter = _movementStateProp.GetSetMethod(true);
                setter?.Invoke(movement, new object[] { FPSMovementState.Sprinting });
            }
        }
    }

    private static void SetInputDirection(Vector2 dir)
    {
        var movement = FindMovement();
        if (movement != null && _inputDirectionField != null)
        {
            _inputDirectionField.SetValue(movement, dir);
        }
    }

    // ====================================================================
    //  PUBLIC STATIC API  - All return string for unity-cli exec
    // ====================================================================

    // -------------------- Movement --------------------

    /// <summary>Set movement direction. x = strafe(-1~1), y = forward(-1~1).</summary>
    public static string Move(float x, float y, float duration = 0.5f)
    {
        EnsureInstance();
        if (FindMovement() == null) return "ERROR: Player not found";

        _instance._moveOverride = new Vector2(
            Mathf.Clamp(x, -1f, 1f),
            Mathf.Clamp(y, -1f, 1f));
        _instance._moveActive = true;
        _instance._moveExpiry = Time.time + duration;
        return $"Move({x},{y}) for {duration}s";
    }

    /// <summary>Walk forward.</summary>
    public static string Forward(float duration = 1f) => Move(0f, 1f, duration);

    /// <summary>Walk backward.</summary>
    public static string Back(float duration = 1f) => Move(0f, -1f, duration);

    /// <summary>Strafe left.</summary>
    public static string Left(float duration = 1f) => Move(-1f, 0f, duration);

    /// <summary>Strafe right.</summary>
    public static string Right(float duration = 1f) => Move(1f, 0f, duration);

    /// <summary>Stop all movement immediately.</summary>
    public static string Stop()
    {
        EnsureInstance();
        _instance._moveActive = false;
        _instance._sprintOverrideActive = false;
        _instance._moveOverride = Vector2.zero;
        SetInputDirection(Vector2.zero);
        return "Stopped";
    }

    /// <summary>Sprint forward for duration.</summary>
    public static string Sprint(float duration = 2f)
    {
        EnsureInstance();
        var movement = FindMovement();
        if (movement == null) return "ERROR: Player not found";

        // 1. Set forward movement
        _instance._moveOverride = new Vector2(0f, 1f);
        _instance._moveActive = true;
        _instance._sprintOverrideActive = true;
        _instance._moveExpiry = Time.time + duration;
        SetInputDirection(new Vector2(0f, 1f));

        // 2. Force sprint state via reflection
        if (_movementStateProp != null)
        {
            var setter = _movementStateProp.GetSetMethod(true);
            setter?.Invoke(movement, new object[] { FPSMovementState.Sprinting });
        }

        return $"Sprint for {duration}s";
    }

    /// <summary>Jump.</summary>
    public static string Jump()
    {
        var movement = FindMovement();
        if (movement == null) return "ERROR: Player not found";
        movement.OnJump();
        return "Jump";
    }

    /// <summary>Toggle crouch.</summary>
    public static string Crouch()
    {
        var movement = FindMovement();
        if (movement == null) return "ERROR: Player not found";
        movement.OnCrouch();
        return $"Crouch toggled -> {movement.PoseState}";
    }

    // -------------------- Combat --------------------

    /// <summary>Start firing (hold trigger).</summary>
    public static string Fire()
    {
        var handler = FindInputHandler();
        if (handler == null) return "ERROR: Player not found";
        handler.ProcessFireInput(true);
        return "Fire ON";
    }

    /// <summary>Release trigger.</summary>
    public static string StopFire()
    {
        var handler = FindInputHandler();
        if (handler == null) return "ERROR: Player not found";
        handler.ProcessFireInput(false);
        return "Fire OFF";
    }

    /// <summary>Fire a single shot (auto-releases after 1 frame).</summary>
    public static string Shoot()
    {
        EnsureInstance();
        var handler = FindInputHandler();
        if (handler == null) return "ERROR: Player not found";
        handler.ProcessFireInput(true);
        _instance.StartCoroutine(DelayedFireRelease());
        return "Shot";
    }

    private static IEnumerator DelayedFireRelease()
    {
        yield return null; // wait 1 frame
        var handler = FindInputHandler();
        handler?.ProcessFireInput(false);
    }

    /// <summary>Toggle aim down sights.</summary>
    public static string Aim(bool enable = true)
    {
        var handler = FindInputHandler();
        var ctrl = FindLocalController();
        if (handler == null || ctrl == null) return "ERROR: Player not found";

        var getActiveItem = ctrl.GetType().GetMethod("GetActiveItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var activeItem = getActiveItem != null ? getActiveItem.Invoke(ctrl, null) : null;
        if (activeItem is not Demo.Scripts.Runtime.Item.Weapon)
        {
            var weaponManager = ctrl.GetComponent<Demo.Scripts.Runtime.Character.FPSWeaponManager>();
            if (weaponManager != null)
            {
                var dbField = weaponManager.GetType().GetField("weaponDatabase", BindingFlags.Instance | BindingFlags.NonPublic);
                var weaponDatabase = dbField?.GetValue(weaponManager) as WeaponData[];
                if (weaponDatabase != null)
                {
                    foreach (var candidate in weaponDatabase)
                    {
                        if (candidate == null) continue;
                        weaponManager.EquipWeapon(candidate);
                        break;
                    }
                }
            }
        }

        bool currentlyAiming = false;
        if (_aimStateField != null)
        {
            var state = (FPSAimState)_aimStateField.GetValue(ctrl);
            currentlyAiming = state is FPSAimState.Aiming or FPSAimState.PointAiming;
        }

        handler.ProcessAimInput(enable, currentlyAiming);

        if (_aimStateField != null)
        {
            var state = (FPSAimState)_aimStateField.GetValue(ctrl);
            bool aimingNow = state is FPSAimState.Aiming or FPSAimState.PointAiming;
            if (aimingNow != enable)
            {
                var handleAimChanged = ctrl.GetType().GetMethod("HandleInputAimChanged", BindingFlags.Instance | BindingFlags.NonPublic);
                handleAimChanged?.Invoke(ctrl, new object[] { enable });
            }
        }

        return enable ? "Aim ON" : "Aim OFF";
    }

    /// <summary>Reload weapon.</summary>
    public static string Reload()
    {
        var ctrl = FindLocalController();
        if (ctrl == null) return "ERROR: Player not found";

        var getActiveItem = ctrl.GetType().GetMethod("GetActiveItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var activeItem = getActiveItem != null ? getActiveItem.Invoke(ctrl, null) : null;
        var weapon = activeItem as Demo.Scripts.Runtime.Item.Weapon;
        var sb = new StringBuilder();
        int forcedWeaponId = -1;

        sb.Append("ReloadDebug");
        sb.Append(" item=").Append(activeItem != null ? activeItem.GetType().Name : "null");

        if (weapon == null)
        {
            var weaponManager = ctrl.GetComponent<Demo.Scripts.Runtime.Character.FPSWeaponManager>();
            if (weaponManager != null)
            {
                var dbField = weaponManager.GetType().GetField("weaponDatabase", BindingFlags.Instance | BindingFlags.NonPublic);
                var weaponDatabase = dbField?.GetValue(weaponManager) as WeaponData[];
                if (weaponDatabase != null)
                {
                    foreach (var candidate in weaponDatabase)
                    {
                        if (candidate == null) continue;
                        forcedWeaponId = System.Array.IndexOf(weaponDatabase, candidate);
                        weaponManager.EquipWeapon(candidate);
                        break;
                    }
                }
            }

            activeItem = getActiveItem != null ? getActiveItem.Invoke(ctrl, null) : null;
            weapon = activeItem as Demo.Scripts.Runtime.Item.Weapon;
            sb.Append(" reequipped=").Append(weapon != null ? weapon.name : "null");

            if (weapon != null)
            {
                var bindMethod = ctrl.GetType().GetMethod("HandleEquippedItemChanged", BindingFlags.Instance | BindingFlags.NonPublic);
                bindMethod?.Invoke(ctrl, new object[] { weapon });
            }
        }

        if (weapon != null)
        {
            sb.Append(" weapon=").Append(weapon.name);
            sb.Append(" ammoBefore=").Append(weapon.CurrentAmmo);
            sb.Append(" reserveBefore=").Append(weapon.GetReserveAmmoFromInventory());

            if (weapon.WeaponData != null && weapon.CurrentAmmo >= weapon.WeaponData.magazineSize)
            {
                var currentAmmoField = typeof(Demo.Scripts.Runtime.Item.Weapon)
                    .GetField("_currentAmmo", BindingFlags.Instance | BindingFlags.NonPublic);

                if (currentAmmoField != null)
                {
                    object syncVarBoxed = currentAmmoField.GetValue(weapon);
                    var valueProp = syncVarBoxed?.GetType().GetProperty("value");
                    if (valueProp != null)
                    {
                        valueProp.SetValue(syncVarBoxed, weapon.WeaponData.magazineSize - 1);
                        currentAmmoField.SetValue(weapon, syncVarBoxed);
                        sb.Append(" magAdjusted=1");
                    }
                }
            }

            if (weapon.GetReserveAmmoFromInventory() <= 0)
            {
                var inventoryManager = FindAnyObjectByType<InventoryManager>();
                if (inventoryManager != null && weapon.WeaponData != null)
                {
                    string ammoItemName = weapon.WeaponData.GetAmmoItemNameKey();
                    if (!string.IsNullOrEmpty(ammoItemName))
                    {
                        var ammoItem = inventoryManager.GetItemByName(ammoItemName);
                        if (ammoItem != null)
                        {
                            inventoryManager.AddItem(ammoItem);
                            sb.Append(" reserveAdded=1");
                        }
                    }
                }
            }

            var refreshReserveCache = typeof(Demo.Scripts.Runtime.Item.Weapon)
                .GetMethod("RefreshReserveCache", BindingFlags.Instance | BindingFlags.NonPublic);
            refreshReserveCache?.Invoke(weapon, null);

            if (weapon.GetReserveAmmoFromInventory() <= 0 && weapon.WeaponData != null)
            {
                var cachedReserveField = typeof(Demo.Scripts.Runtime.Item.Weapon)
                    .GetField("_cachedReserveAmmo", BindingFlags.Instance | BindingFlags.NonPublic);
                cachedReserveField?.SetValue(weapon, weapon.WeaponData.magazineSize);
            }

            sb.Append(" ammoReady=").Append(weapon.CurrentAmmo);
            sb.Append(" reserveReady=").Append(weapon.GetReserveAmmoFromInventory());
        }

        if (weapon != null)
        {
            bool started = weapon.OnReload();
            sb.Append(" weaponReload=").Append(started ? 1 : 0);
            sb.Append(" reloadBridge=").Append(started ? 1 : 0);

            return sb.ToString();
        }

        var handler = FindInputHandler();
        if (handler == null) return "ERROR: Player not found";
        handler.ProcessReloadInput();
        sb.Append(" weaponReload=0 fallbackInput=1");
        return sb.ToString();
    }

    /// <summary>Throw grenade.</summary>
    public static string Grenade()
    {
        var handler = FindInputHandler();
        if (handler == null) return "ERROR: Player not found";
        handler.ProcessGrenadeThrowInput();
        return "Grenade";
    }

    // -------------------- Camera --------------------

    /// <summary>
    /// Rotate camera. yaw = horizontal (+ right), pitch = vertical (+ up).
    /// Values are in degrees.
    /// </summary>
    public static string Look(float yaw, float pitch)
    {
        var ctrl = FindLocalController();
        var look = FindLookController();
        if (ctrl == null) return "ERROR: Player not found";

        // 1. Yaw: rotate player transform
        ctrl.transform.Rotate(0f, yaw, 0f);

        // 2. Pitch: update accumulated look input
        if (look != null)
        {
            Vector2 current = look.PlayerInput;
            // _playerInput.y negative = look up, positive = look down
            Vector2 newInput = new Vector2(
                current.x + yaw,
                Mathf.Clamp(current.y - pitch, -90f, 90f));
            look.SetPlayerInput(newInput);

            // 3. Sync Kinemation animation input
            var userInput = ctrl.GetComponent<UserInputController>();
            if (userInput != null)
            {
                userInput.SetValue(FPSANames.MouseDeltaInput, new Vector4(yaw, -pitch));
                userInput.SetValue(FPSANames.MouseInput, new Vector4(newInput.x, newInput.y));
            }
        }

        return $"Look yaw={yaw} pitch={pitch}";
    }

    /// <summary>Look right by degrees.</summary>
    public static string LookRight(float deg = 30f) => Look(deg, 0f);

    /// <summary>Look left by degrees.</summary>
    public static string LookLeft(float deg = 30f) => Look(-deg, 0f);

    /// <summary>Look up by degrees.</summary>
    public static string LookUp(float deg = 15f) => Look(0f, deg);

    /// <summary>Look down by degrees.</summary>
    public static string LookDown(float deg = 15f) => Look(0f, -deg);

    // -------------------- Perception --------------------

    /// <summary>
    /// Raycast forward and report what the player is looking at.
    /// maxDist = max ray distance in meters.
    /// </summary>
    public static string Scan(float maxDist = 50f)
    {
        var ctrl = FindLocalController();
        if (ctrl == null) return "ERROR: Player not found";

        var cam = ctrl.GetComponentInChildren<Camera>(true);
        Transform origin = cam != null ? cam.transform : ctrl.transform;

        var sb = new StringBuilder();
        sb.AppendLine($"=== Scan (forward {maxDist}m) ===");

        // Single raycast for center of view
        if (Physics.Raycast(origin.position, origin.forward, out RaycastHit hit, maxDist))
        {
            sb.AppendLine($"[HIT] {hit.collider.gameObject.name}  dist={hit.distance:F1}m");
            sb.AppendLine($"  pos={hit.point:F2}  tag={hit.collider.tag}  layer={LayerMask.LayerToName(hit.collider.gameObject.layer)}");

            // Walk up hierarchy to find meaningful parent
            Transform t = hit.collider.transform;
            string hierarchy = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                hierarchy = t.name + "/" + hierarchy;
            }
            sb.AppendLine($"  hierarchy={hierarchy}");
        }
        else
        {
            sb.AppendLine("[MISS] Nothing hit within range");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Find all objects with colliders within radius around the player.
    /// Returns name, distance, direction for each.
    /// </summary>
    public static string Nearby(float radius = 15f, int maxResults = 20)
    {
        var ctrl = FindLocalController();
        if (ctrl == null) return "ERROR: Player not found";

        Vector3 pos = ctrl.transform.position;
        Vector3 fwd = ctrl.transform.forward;

        var cols = Physics.OverlapSphere(pos, radius);
        if (cols.Length == 0) return $"Nothing within {radius}m";

        // Deduplicate by root gameobject name and sort by distance
        var seen = new System.Collections.Generic.Dictionary<string, (float dist, Vector3 objPos, string tag)>();
        foreach (var col in cols)
        {
            // Skip player's own colliders
            if (col.transform.IsChildOf(ctrl.transform)) continue;

            // Use top-level meaningful parent (first child of root, or root)
            Transform root = col.transform;
            while (root.parent != null && root.parent.parent != null)
                root = root.parent;

            string key = root.name;
            float dist = Vector3.Distance(pos, col.transform.position);

            if (!seen.ContainsKey(key) || seen[key].dist > dist)
                seen[key] = (dist, col.transform.position, col.tag);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"=== Nearby ({radius}m, {seen.Count} objects) ===");

        // Sort by distance
        var sorted = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, (float dist, Vector3 objPos, string tag)>>(seen);
        sorted.Sort((a, b) => a.Value.dist.CompareTo(b.Value.dist));

        int count = 0;
        foreach (var kv in sorted)
        {
            if (count >= maxResults) { sb.AppendLine($"  ... +{sorted.Count - maxResults} more"); break; }

            Vector3 dir = (kv.Value.objPos - pos).normalized;
            float angle = Vector3.SignedAngle(fwd, new Vector3(dir.x, 0, dir.z), Vector3.up);
            string compass = angle switch
            {
                > -22.5f and <= 22.5f => "front",
                > 22.5f and <= 67.5f => "front-R",
                > 67.5f and <= 112.5f => "right",
                > 112.5f and <= 157.5f => "back-R",
                > 157.5f or <= -157.5f => "back",
                > -157.5f and <= -112.5f => "back-L",
                > -112.5f and <= -67.5f => "left",
                _ => "front-L"
            };

            string tag = kv.Value.tag != "Untagged" ? $" [{kv.Value.tag}]" : "";
            sb.AppendLine($"  {kv.Value.dist:F1}m {compass,-8} {kv.Key}{tag}");
            count++;
        }

        return sb.ToString();
    }

    // -------------------- Utility --------------------

    /// <summary>Teleport to world coordinates.</summary>
    public static string Teleport(float x, float y, float z)
    {
        if (!TryTeleportPlayer(new Vector3(x, y, z), Quaternion.identity, false))
            return "ERROR: Player not found";

        return $"Teleported to ({x}, {y}, {z})";
    }

    // -------------------- Dungeon --------------------

    /// <summary>Generate a dungeon immediately. If one is already active, regenerate it.</summary>
    public static string GenerateDungeon()
    {
        var controller = FindDungeonController();
        if (controller == null) return "ERROR: NetworkDungeonController not found";
        if (!controller.isServer) return "ERROR: Dungeon generation requires host/server context";

        bool wasActive = controller.IsDungeonActive;
        if (wasActive)
            controller.ClearDungeonServer();

        controller.StartDungeonServer();

        if (!controller.IsDungeonActive)
            return "ERROR: Dungeon generation did not activate";

        string mode = wasActive ? "Regenerated" : "Generated";
        return $"{mode} dungeon flowIndex={controller.CurrentFlowIndex} seed={controller.CurrentSeed}";
    }

    /// <summary>Teleport the local player to a random dungeon point sampled onto the NavMesh.</summary>
    public static string GoToRandomDungeonArea(float navmeshRadius = 3f)
    {
        if (!TryResolveRandomDungeonDestination(Mathf.Max(0.5f, navmeshRadius), out var sourcePoint, out var destination, out var sourceLabel))
            return "ERROR: No valid dungeon point found. Generate the dungeon first.";

        Quaternion rotation = sourcePoint != null ? sourcePoint.rotation : Quaternion.identity;
        if (!TryTeleportPlayer(destination, rotation, sourcePoint != null))
            return "ERROR: Player not found";

        var zoneManager = FindDungeonZoneManager();
        var playerRoot = FindLocalPlayerRoot();
        if (zoneManager != null && playerRoot != null)
            zoneManager.EnterDungeon(playerRoot);

        return $"Moved to random dungeon area via {sourceLabel} -> {destination}";
    }

    public static string BeginWaitForDungeonReady(float timeoutSeconds = 12f)
    {
        EnsureInstance();

        _instance._dungeonReadyResult = "PENDING";
        _instance.StartCoroutine(_instance.WaitForDungeonReadyRoutine(Mathf.Max(0.5f, timeoutSeconds)));
        return "STARTED";
    }

    public static string GetDungeonReadyResult()
    {
        EnsureInstance();
        return string.IsNullOrEmpty(_instance._dungeonReadyResult) ? "NONE" : _instance._dungeonReadyResult;
    }

    /// <summary>Spawn one or more monsters near the local player.</summary>
    public static string SpawnMonster(string monsterName = null, int count = 1, float radius = 8f)
    {
        var spawner = FindMonsterSpawner();
        if (spawner == null) return "ERROR: DungeonMonsterSpawner not found";

        var playerRoot = FindLocalPlayerRoot();
        if (playerRoot == null) return "ERROR: Player not found";

        int targetCount = Mathf.Max(1, count);
        var sb = new StringBuilder();
        int spawnedCount = 0;

        for (int i = 0; i < targetCount; i++)
        {
            if (!spawner.TrySpawnDebugMonster(monsterName, playerRoot.position, radius, out var spawned, out var matchedName, out var error))
            {
                if (spawnedCount == 0)
                    return $"ERROR: {error}";

                sb.AppendLine($"Stopped after {spawnedCount}: {error}");
                break;
            }

            spawnedCount++;
            sb.AppendLine($"[{spawnedCount}] {matchedName} @ {spawned.transform.position}");
        }

        return $"Spawned {spawnedCount} monster(s) near player\n{sb}";
    }

    public static string LaunchCurrentClownGiftAtPlayer(float speed = 15f, float forwardOffset = 2f, float heightOffset = 1f)
    {
        if (!HasAuthoritativeDebugControl())
            return "ERROR: Clown gift debug commands require host/server authority";

        var playerRoot = FindLocalPlayerRoot();
        if (playerRoot == null) return "ERROR: Player not found";

        var gift = FindRuntimeClownGift();
        if (gift == null) return "ERROR: ClownGift not found";

        var rb = gift.GetComponent<Rigidbody>();
        if (rb == null) return "ERROR: Gift rigidbody not found";

        gift.CancelInvoke();
        rb.constraints = RigidbodyConstraints.None;
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        gift.transform.position = playerRoot.position + playerRoot.forward * forwardOffset + Vector3.up * heightOffset;
        rb.linearVelocity = -playerRoot.forward * Mathf.Max(0.5f, speed);

        return $"Launched clown gift at player speed={speed:F1}";
    }

    public static string BeginClownGiftImpactTest(float speed = 0f, float forwardOffset = 0.25f, float heightOffset = 0f, float resultDelay = 0.2f)
    {
        if (!HasAuthoritativeDebugControl())
            return "ERROR: Clown gift debug commands require host/server authority";

        EnsureInstance();
        var playerRoot = FindLocalPlayerRoot();
        if (playerRoot == null) return "ERROR: Player not found";

        var actor = Object.FindFirstObjectByType<ClownMonsterActor>();
        if (actor == null) return "ERROR: ClownMonsterActor not found";

        var throwGift = typeof(ClownMonsterActor).GetMethod("ThrowGift", BindingFlags.NonPublic | BindingFlags.Instance);
        if (throwGift == null) return "ERROR: ThrowGift method not found";

        var existingGifts = new HashSet<ClownGift>(Object.FindObjectsByType<ClownGift>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        throwGift.Invoke(actor, new object[] { playerRoot.position });

        var gift = FindRuntimeClownGift(existingGifts);
        if (gift == null) return "ERROR: ClownGift not found after throw";

        var rb = gift.GetComponent<Rigidbody>();
        if (rb == null) return "ERROR: Gift rigidbody not found";

        gift.CancelInvoke();
        rb.constraints = RigidbodyConstraints.None;
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        Vector3 impactPoint = ResolvePlayerImpactProbePoint(playerRoot) + Vector3.up * heightOffset;
        Vector3 offsetDirection = playerRoot.forward;
        offsetDirection.y = 0f;
        if (offsetDirection.sqrMagnitude <= 0.0001f)
            offsetDirection = Vector3.forward;
        else
            offsetDirection.Normalize();

        float resolvedOffset = Mathf.Clamp(forwardOffset, 0.05f, 0.5f);
        Vector3 probePosition = impactPoint + offsetDirection * resolvedOffset;
        gift.transform.position = probePosition;
        rb.position = probePosition;
        Physics.SyncTransforms();
        rb.linearVelocity = speed > 0.01f
            ? (impactPoint - gift.transform.position).normalized * speed
            : Vector3.zero;
        rb.useGravity = speed > 0.01f;

        _instance._clownGiftImpactTestResult = "PENDING";
        _instance.StartCoroutine(_instance.CaptureClownGiftImpactResult(gift, Mathf.Max(0.1f, resultDelay)));
        return "STARTED";
    }

    public static string GetClownGiftImpactTestResult()
    {
        EnsureInstance();
        return string.IsNullOrEmpty(_instance._clownGiftImpactTestResult) ? "NONE" : _instance._clownGiftImpactTestResult;
    }

    private IEnumerator CaptureClownGiftImpactResult(ClownGift gift, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (gift == null)
        {
            _clownGiftImpactTestResult = "MISSING_AFTER_DELAY";
            yield break;
        }

        var rb = gift.GetComponent<Rigidbody>();
        if (rb == null)
        {
            _clownGiftImpactTestResult = "NO_RIGIDBODY";
            yield break;
        }

        bool impactRegistered = gift.HasStoppedOnPlayerImpact && !gift.HasExploded;
        _clownGiftImpactTestResult = impactRegistered
            ? "OK"
            : $"FAIL impacted={gift.HasStoppedOnPlayerImpact} exploded={gift.HasExploded} hold={gift.IsPlayerImpactHoldActive} gravity={rb.useGravity} kinematic={rb.isKinematic} constraints={rb.constraints} vel={rb.linearVelocity}";
    }

    private static bool HasAuthoritativeDebugControl()
    {
        NetworkManager networkManager = NetworkManager.main;
        return networkManager == null || networkManager.isServer;
    }

    private static ClownGift FindRuntimeClownGift(HashSet<ClownGift> excluded = null)
    {
        ClownGift fallback = null;
        ClownGift[] gifts = Object.FindObjectsByType<ClownGift>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < gifts.Length; i++)
        {
            ClownGift gift = gifts[i];
            if (gift == null || excluded != null && excluded.Contains(gift))
                continue;

            if (gift.name.Contains("Runtime"))
                return gift;

            fallback ??= gift;
        }

        return fallback;
    }

    private static Vector3 ResolvePlayerImpactProbePoint(Transform playerRoot)
    {
        if (playerRoot == null)
            return Vector3.zero;

        Collider[] colliders = playerRoot.GetComponentsInChildren<Collider>(true);
        bool hasBounds = false;
        Bounds bounds = default;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds ? bounds.center : playerRoot.position + Vector3.up;
    }

    private IEnumerator WaitForDungeonReadyRoutine(float timeoutSeconds)
    {
        float endTime = Time.time + timeoutSeconds;
        while (Time.time < endTime)
        {
            int spawnCount = DungeonPointRegistry.SpawnPoints.Count;
            int patrolCount = DungeonPointRegistry.PatrolPoints.Count;
            var controller = FindDungeonController();

            if (controller != null && controller.IsDungeonActive && (spawnCount > 0 || patrolCount > 0))
            {
                _dungeonReadyResult = $"OK spawn={spawnCount} patrol={patrolCount}";
                yield break;
            }

            yield return new WaitForSeconds(0.25f);
        }

        _dungeonReadyResult = $"TIMEOUT spawn={DungeonPointRegistry.SpawnPoints.Count} patrol={DungeonPointRegistry.PatrolPoints.Count}";
    }

    public static string InspectCurrentClownGift()
    {
        var gift = Object.FindFirstObjectByType<ClownGift>();
        if (gift == null) return "ERROR: ClownGift not found";

        var rb = gift.GetComponent<Rigidbody>();
        if (rb == null) return "ERROR: Gift rigidbody not found";

        return $"Gift pos={gift.transform.position} vel={rb.linearVelocity} gravity={rb.useGravity} constraints={rb.constraints} kinematic={rb.isKinematic}";
    }

    /// <summary>Spawn one or more items near the local player.</summary>
    public static string SpawnItem(string itemName = null, int count = 1, float radius = 3f)
    {
        var loot = FindLootPostProcessor();
        if (loot == null) return "ERROR: DungeonLootPostProcessor not found";

        var playerRoot = FindLocalPlayerRoot();
        if (playerRoot == null) return "ERROR: Player not found";

        int targetCount = Mathf.Max(1, count);
        var sb = new StringBuilder();
        int spawnedCount = 0;

        for (int i = 0; i < targetCount; i++)
        {
            if (!loot.TrySpawnDebugItem(itemName, playerRoot.position, radius, out var spawned, out var matchedName, out var error))
            {
                if (spawnedCount == 0)
                    return $"ERROR: {error}";

                sb.AppendLine($"Stopped after {spawnedCount}: {error}");
                break;
            }

            spawnedCount++;
            sb.AppendLine($"[{spawnedCount}] {matchedName} @ {spawned.transform.position}");
        }

        return $"Spawned {spawnedCount} item(s) near player\n{sb}";
    }

    /// <summary>List players, monsters, items, and dungeon registry counts around the local player.</summary>
    public static string ListDungeonEntities(float radius = 40f, int maxResults = 12)
    {
        var playerRoot = FindLocalPlayerRoot();
        Vector3 origin = playerRoot != null ? playerRoot.position : Vector3.zero;

        var controller = FindDungeonController();
        var spawner = FindMonsterSpawner();
        var loot = FindLootPostProcessor();
        var monsters = FindObjectsByType<MonsterHealth>(FindObjectsSortMode.None);
        var items = FindObjectsByType<Item>(FindObjectsSortMode.None);
        var players = FindObjectsByType<FPSController>(FindObjectsSortMode.None);

        var sb = new StringBuilder();
        sb.AppendLine("=== Dungeon Entities ===");
        sb.AppendLine($"Scene:       {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        sb.AppendLine($"Dungeon:     {(controller != null && controller.IsDungeonActive ? "Active" : "Inactive")}");
        if (controller != null)
            sb.AppendLine($"Flow/Seed:   {controller.CurrentFlowIndex} / {controller.CurrentSeed}");
        sb.AppendLine($"Players:     {players.Length}");
        sb.AppendLine($"Monsters:    {monsters.Length} (spawner alive={spawner?.AliveCount ?? 0}, configured={spawner?.ConfiguredMonsterCount ?? 0})");
        sb.AppendLine($"Items:       {items.Length} (loot root={loot?.LootCount ?? 0})");
        sb.AppendLine($"Registry:    spawn={DungeonPointRegistry.SpawnPoints.Count}, patrol={DungeonPointRegistry.PatrolPoints.Count}");

        if (playerRoot == null)
            return sb.ToString();

        AppendNearestMonsters(sb, monsters, origin, radius, maxResults);
        AppendNearestItems(sb, items, origin, radius, maxResults);

        return sb.ToString();
    }

    private static void AppendNearestMonsters(StringBuilder sb, MonsterHealth[] monsters, Vector3 origin, float radius, int maxResults)
    {
        var nearby = new List<MonsterHealth>();
        float maxDistance = Mathf.Max(0f, radius);

        for (int i = 0; i < monsters.Length; i++)
        {
            var monster = monsters[i];
            if (monster == null)
                continue;

            if (Vector3.Distance(origin, monster.transform.position) <= maxDistance)
                nearby.Add(monster);
        }

        nearby.Sort((a, b) => Vector3.Distance(origin, a.transform.position).CompareTo(Vector3.Distance(origin, b.transform.position)));
        sb.AppendLine($"Nearby Monsters ({nearby.Count}):");

        for (int i = 0; i < nearby.Count && i < maxResults; i++)
        {
            var monster = nearby[i];
            float distance = Vector3.Distance(origin, monster.transform.position);
            sb.AppendLine($"  {distance:F1}m {monster.name} hp={monster.Health}/{monster.MaxHealth} dead={monster.IsDead}");
        }
    }

    private static void AppendNearestItems(StringBuilder sb, Item[] items, Vector3 origin, float radius, int maxResults)
    {
        var nearby = new List<Item>();
        float maxDistance = Mathf.Max(0f, radius);

        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item == null)
                continue;

            if (Vector3.Distance(origin, item.transform.position) <= maxDistance)
                nearby.Add(item);
        }

        nearby.Sort((a, b) => Vector3.Distance(origin, a.transform.position).CompareTo(Vector3.Distance(origin, b.transform.position)));
        sb.AppendLine($"Nearby Items ({nearby.Count}):");

        for (int i = 0; i < nearby.Count && i < maxResults; i++)
        {
            var item = nearby[i];
            float distance = Vector3.Distance(origin, item.transform.position);
            string name = string.IsNullOrWhiteSpace(item.ItemName) ? item.name : item.ItemName;
            sb.AppendLine($"  {distance:F1}m {name} price={item.Price}");
        }
    }

    /// <summary>Quick position check.</summary>
    public static string Pos()
    {
        var ctrl = FindLocalController();
        if (ctrl == null) return "ERROR: Player not found";
        Vector3 p = ctrl.transform.position;
        return $"({p.x:F2}, {p.y:F2}, {p.z:F2})";
    }

    /// <summary>Player status overview.</summary>
    public static string Status()
    {
        var ctrl = FindLocalController();
        if (ctrl == null) return "ERROR: Player not found";

        var movement = ctrl.GetComponent<FPSMovement>();
        var sb = new StringBuilder();

        sb.AppendLine("=== Player Status ===");
        sb.AppendLine($"Position:  {ctrl.transform.position:F2}");
        sb.AppendLine($"Rotation:  {ctrl.transform.eulerAngles:F1}");

        if (movement != null)
        {
            sb.AppendLine($"Movement:  {movement.MovementState}");
            sb.AppendLine($"Pose:      {movement.PoseState}");
            sb.AppendLine($"Speed:     {movement.GetSpeed():F2}");
            sb.AppendLine($"InAir:     {movement.IsInAir()}");
            sb.AppendLine($"Moving:    {movement.IsMoving()}");
        }

        if (_aimStateField != null)
            sb.AppendLine($"AimState:  {_aimStateField.GetValue(ctrl)}");

        var actionField = typeof(FPSController).GetField(
            "_actionState", BindingFlags.NonPublic | BindingFlags.Instance);
        if (actionField != null)
            sb.AppendLine($"Action:    {actionField.GetValue(ctrl)}");

        var look = ctrl.GetComponent<FPSLookController>();
        if (look != null)
            sb.AppendLine($"LookInput: {look.PlayerInput}");

        sb.AppendLine($"RemoteMove: {(_instance != null && _instance._moveActive ? _instance._moveOverride.ToString() : "OFF")}");

        return sb.ToString();
    }

    /// <summary>Multiplayer/runtime snapshot for dual-editor verification.</summary>
    public static string MultiplayerProbe()
    {
        var sb = new StringBuilder();
        var nm = NetworkManager.main;
        var currency = Object.FindFirstObjectByType<CurrencyManager>();
        var weaponItems = Object.FindObjectsByType<WeaponShopItem>(FindObjectsSortMode.None);
        var bagItems = Object.FindObjectsByType<InventoryBagItem>(FindObjectsSortMode.None);
        var players = Object.FindObjectsByType<FPSMovement>(FindObjectsSortMode.None);
        var localCtrl = FindLocalController();

        bool isClone = false;
#if UNITY_EDITOR
        isClone = ParrelSync.ClonesManager.IsClone();
#endif

        sb.AppendLine("=== Multiplayer Probe ===");
        sb.AppendLine($"Project:    {Application.dataPath}");
        sb.AppendLine($"Scene:      {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        sb.AppendLine($"IsClone:    {isClone}");

        if (nm != null)
        {
            sb.AppendLine($"Server:     {nm.isServer}");
            sb.AppendLine($"Client:     {nm.isClient}");
            sb.AppendLine($"Host:       {nm.isHost}");
            sb.AppendLine($"ServerState:{nm.serverState}");
            sb.AppendLine($"ClientState:{nm.clientState}");
        }
        else
        {
            sb.AppendLine("Network:    no NetworkManager.main");
        }

        sb.AppendLine($"Players:    {players.Length}");
        sb.AppendLine($"LocalCtrl:  {(localCtrl != null ? localCtrl.name : "null")}");
        sb.AppendLine($"Currency:   {(currency != null ? currency.SharedCurrency.ToString() : "null")}");
        sb.AppendLine($"WeaponShop: {weaponItems.Length}");
        sb.AppendLine($"BagShop:    {bagItems.Length}");

        var weaponShop = Object.FindFirstObjectByType<WeaponShop>();
        var bagShop = Object.FindFirstObjectByType<InventoryExpansionShop>();
        sb.AppendLine($"WeaponShopObj: {(weaponShop != null ? weaponShop.name : "null")}");
        sb.AppendLine($"BagShopObj:    {(bagShop != null ? bagShop.name : "null")}");

        if (weaponShop != null)
        {
            sb.AppendLine($"ShopPrefabSlots:   {weaponShop.PrefabSlotCount}");
            sb.AppendLine($"WeaponPrefabSlots: {weaponShop.WeaponPrefabCount}");
            sb.AppendLine($"AmmoPrefabSlots:   {weaponShop.AmmoPrefabCount}");
            sb.AppendLine($"WeaponHierarchy:   {weaponShop.GetComponentsInChildren<WeaponShopItem>(true).Length}");
            sb.AppendLine($"WeaponRepDebug:    {WeaponShop.GetReplicationDebug()}");
        }

        if (bagShop != null)
        {
            sb.AppendLine($"BagHierarchy:      {bagShop.GetComponentsInChildren<InventoryBagItem>(true).Length}");
        }

        var weather = FindWeatherController();
        if (weather != null)
        {
            sb.AppendLine($"WeatherMode:       {weather.CurrentResolvedPrecipitationMode}");
            sb.AppendLine($"WeatherIntensity:  {weather.CurrentResolvedPrecipitationIntensity:F2}");
            sb.AppendLine($"WeatherClouds:     {weather.CurrentResolvedCloudiness:F2}");
        }

        return sb.ToString();
    }

    public static string InventoryProbe()
    {
        var inventory = Object.FindFirstObjectByType<InventoryManager>();
        if (inventory == null)
            return "ERROR: InventoryManager not found";

        var field = typeof(InventoryManager).GetField("_inventoryData", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
            return "ERROR: _inventoryData field not found";

        var raw = field.GetValue(inventory);
        if (raw is not System.Array array)
            return "ERROR: inventory data unavailable";

        var sb = new StringBuilder();
        sb.AppendLine("=== Inventory Probe ===");

        int count = 0;
        for (int i = 0; i < array.Length; i++)
        {
            object entry = array.GetValue(i);
            if (entry == null)
                continue;

            System.Type entryType = entry.GetType();
            string itemName = entryType.GetField("itemName")?.GetValue(entry) as string;
            if (string.IsNullOrEmpty(itemName))
                continue;

            int ammo = (int)(entryType.GetField("currentAmmo")?.GetValue(entry) ?? 0);
            int tier = (int)(entryType.GetField("upgradeTier")?.GetValue(entry) ?? 0);
            int price = (int)(entryType.GetField("price")?.GetValue(entry) ?? 0);
            sb.AppendLine($"[{i}] {itemName} tier=+{tier} ammo={ammo} price={price}");
            count++;
        }

        if (count == 0)
            sb.AppendLine("(empty)");

        var active = inventory.GetActiveItemData();
        if (active.HasValue)
            sb.AppendLine($"Active: {active.Value.itemName} tier=+{active.Value.upgradeTier} ammo={active.Value.currentAmmo}");
        else
            sb.AppendLine("Active: none");

        return sb.ToString();
    }

    public static string DropActiveItem()
    {
        var inventory = Object.FindFirstObjectByType<InventoryManager>();
        if (inventory == null)
            return "ERROR: InventoryManager not found";

        return inventory.TryDropActiveItem()
            ? "Dropped active inventory item"
            : "ERROR: Active inventory item not found";
    }

    public static string PickupNearestItem(float radius = 4f)
    {
        var ctrl = FindLocalController();
        if (ctrl == null)
            return "ERROR: Player not found";

        Item nearest = null;
        float bestSqrDistance = radius * radius;
        var items = Object.FindObjectsByType<Item>(FindObjectsSortMode.None);

        for (int i = 0; i < items.Length; i++)
        {
            Item item = items[i];
            if (item == null || !item.gameObject.activeInHierarchy)
                continue;

            float sqrDistance = (item.transform.position - ctrl.transform.position).sqrMagnitude;
            if (sqrDistance > bestSqrDistance)
                continue;

            nearest = item;
            bestSqrDistance = sqrDistance;
        }

        if (nearest == null)
            return $"ERROR: No item within {radius:F1}m";

        string beforeName = nearest.DisplayName;
        nearest.Pickup();
        return $"Pickup requested: {beforeName}";
    }

    public static string AnvilOptions(float radius = 8f)
    {
        var anvil = FindNearestAnvil(radius, out float distance);
        if (anvil == null)
            return $"ERROR: No anvil within {radius:F1}m";

        var options = anvil.DetectAvailableUpgrades();
        var sb = new StringBuilder();
        sb.AppendLine($"=== Anvil Options ({distance:F1}m) ===");

        if (options.Count == 0)
        {
            sb.AppendLine("(none)");
            return sb.ToString();
        }

        for (int i = 0; i < options.Count; i++)
        {
            UpgradeOption option = options[i];
            sb.AppendLine($"[{i}] {option.recipe.baseItemName} high=+{option.highestOwnedTier} result=+{option.resultTier} materials={option.materialCount} cost={option.recipe.upgradeCost}");
        }

        return sb.ToString();
    }

    public static string UpgradeAtNearestAnvil(string baseItemName = "")
    {
        var anvil = FindNearestAnvil(8f, out float distance);
        if (anvil == null)
            return "ERROR: No anvil within 8m";

        var options = anvil.DetectAvailableUpgrades();
        if (options.Count == 0)
            return "ERROR: No upgrade options available";

        UpgradeOption? selected = null;
        for (int i = 0; i < options.Count; i++)
        {
            if (string.IsNullOrEmpty(baseItemName) || options[i].recipe.baseItemName == baseItemName)
            {
                selected = options[i];
                break;
            }
        }

        if (!selected.HasValue)
            return $"ERROR: No matching anvil option for '{baseItemName}'";

        bool success = anvil.TryUpgrade(selected.Value);
        if (!success)
            return $"Upgrade failed: {selected.Value.recipe.baseItemName}";

        return $"Upgraded via anvil ({distance:F1}m): {selected.Value.recipe.baseItemName} -> +{selected.Value.resultTier}";
    }

    private static AnvilInteraction FindNearestAnvil(float radius, out float distance)
    {
        distance = float.PositiveInfinity;
        var ctrl = FindLocalController();
        if (ctrl == null)
            return null;

        AnvilInteraction nearest = null;
        float bestSqrDistance = radius * radius;
        var anvils = Object.FindObjectsByType<AnvilInteraction>(FindObjectsSortMode.None);

        for (int i = 0; i < anvils.Length; i++)
        {
            AnvilInteraction anvil = anvils[i];
            if (anvil == null || !anvil.gameObject.activeInHierarchy)
                continue;

            float sqrDistance = (anvil.transform.position - ctrl.transform.position).sqrMagnitude;
            if (sqrDistance > bestSqrDistance)
                continue;

            nearest = anvil;
            bestSqrDistance = sqrDistance;
        }

        if (nearest != null)
            distance = Mathf.Sqrt(bestSqrDistance);

        return nearest;
    }

    // ==================== Weather / Time ====================

    private static AltosSkyDirector FindSkyDirector()
    {
        return AltosSkyDirector.Instance;
    }

    private static StartMapWeatherController FindWeatherController()
    {
        return Object.FindFirstObjectByType<StartMapWeatherController>();
    }

    private static TimeManager FindTimeManager()
    {
        return Object.FindFirstObjectByType<TimeManager>();
    }

    /// <summary>Set time of day (0-24). e.g. 6=dawn, 12=noon, 18=dusk, 0=midnight.</summary>
    public static string SetTime(float hour)
    {
        var sky = FindSkyDirector();
        if (sky == null || sky.skyDefinition == null) return "ERROR: SkyDirector not found";

        sky.skyDefinition.SetSystemTime(hour);
        return $"Time set to {hour:F1}h ({FormatTime(hour)})";
    }

    /// <summary>Get current time of day.</summary>
    public static string GetTime()
    {
        var sky = FindSkyDirector();
        if (sky == null || sky.skyDefinition == null) return "ERROR: SkyDirector not found";

        float t = sky.skyDefinition.CurrentTime;
        return $"{t:F1}h ({FormatTime(t)}) Day={sky.skyDefinition.CurrentDay} Daytime={sky.daytimeFactor:F2}";
    }

    /// <summary>Set day-night cycle speed. 0=frozen, 0.5=fast, 1=normal(1h=1day).</summary>
    public static string TimeSpeed(float speed)
    {
        var sky = FindSkyDirector();
        if (sky == null || sky.skyDefinition == null) return "ERROR: SkyDirector not found";

        sky.skyDefinition.dayNightCycleDuration = speed;
        return speed <= 0f ? "Time frozen" : $"Time speed = {speed} (1 real hour = 1 game day at 1.0)";
    }

    /// <summary>Set weather to rain.</summary>
    public static string Rain(float intensity = 0.7f)
    {
        return SetWeatherMode("Rain", intensity);
    }

    /// <summary>Set weather to snow.</summary>
    public static string Snow(float intensity = 0.7f)
    {
        return SetWeatherMode("Snow", intensity);
    }

    /// <summary>Clear all precipitation.</summary>
    public static string ClearWeather()
    {
        return SetWeatherMode("Off", 0f);
    }

    /// <summary>Set cloud coverage (0=clear, 1=overcast).</summary>
    public static string Clouds(float amount)
    {
        var wc = FindWeatherController();
        if (wc == null) return "ERROR: WeatherController not found";

        wc.SetManualCloudiness(Mathf.Clamp01(amount));

        return $"Clouds = {amount:P0}";
    }

    /// <summary>Full weather status report.</summary>
    public static string WeatherStatus()
    {
        var sky = FindSkyDirector();
        var tm = FindTimeManager();
        var sb = new StringBuilder();
        sb.AppendLine("=== Weather Status ===");

        if (sky != null && sky.skyDefinition != null)
        {
            float t = sky.skyDefinition.CurrentTime;
            sb.AppendLine($"Time:       {t:F1}h ({FormatTime(t)})");
            sb.AppendLine($"Day:        {sky.skyDefinition.CurrentDay}");
            sb.AppendLine($"Daytime:    {sky.daytimeFactor:F2} (1=noon, 0=midnight)");
            sb.AppendLine($"CycleSpeed: {sky.skyDefinition.dayNightCycleDuration}");
        }

        if (sky != null)
        {
            sb.AppendLine($"Clouds:     {sky.GetCurrentCloudiness():P0}");
            sb.AppendLine($"Precip:     {sky.GetCurrentPrecipitation():P0}");
        }

        if (sky != null && sky.temperatureDefinition != null)
            sb.AppendLine($"Temp:       {sky.temperatureDefinition.currentTemperatureAtSeaLevel:F1}C");

        if (tm != null)
        {
            sb.AppendLine($"Clock:      {(tm.IsClockRunning() ? "Running" : "Stopped")}");
            var (votes, total) = tm.GetSleepVoteStatus();
            sb.AppendLine($"SleepVotes: {votes}/{total}");
        }

        return sb.ToString();
    }

    private static string SetWeatherMode(string mode, float intensity)
    {
        var wc = FindWeatherController();
        if (wc == null) return "ERROR: WeatherController not found";

        var modeEnum = mode switch
        {
            "Rain" => StartMapWeatherController.PrecipitationMode.Rain,
            "Snow" => StartMapWeatherController.PrecipitationMode.Snow,
            "Off"  => StartMapWeatherController.PrecipitationMode.Off,
            _      => StartMapWeatherController.PrecipitationMode.Automatic
        };

        wc.SetManualWeatherMode(modeEnum, Mathf.Clamp01(intensity));

        return $"Weather = {mode} intensity={intensity:F1}";
    }

    private static void SetPrivateField(System.Type type, object obj, string fieldName, object value)
    {
        var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        field?.SetValue(obj, value);
    }

    private static string FormatTime(float hour)
    {
        int h = (int)hour % 24;
        int m = (int)((hour - (int)hour) * 60f);
        return $"{h:D2}:{m:D2}";
    }

    // ==================== Interaction ====================

    /// <summary>
    /// Interact with whatever the player is looking at (simulates F key).
    /// </summary>
    public static string Interact()
    {
        var ctrl = FindLocalController();
        if (ctrl == null) return "ERROR: Player not found";

        // Find InteractionManager in scene
        var im = Object.FindFirstObjectByType<InteractionManager>();
        if (im == null) return "ERROR: InteractionManager not found";

        Camera cam = im.CurrentCamera;
        if (cam == null)
        {
            cam = ctrl.GetComponentInChildren<Camera>(true);
        }
        if (cam == null) return "ERROR: No camera found";

        // Raycast matching InteractionManager logic
        int layerMask = GetInteractableLayerMask();
        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 4f, layerMask))
            return "Nothing in range to interact with";

        var interactables = hit.collider.GetComponentsInParent<AInteractable>();
        if (interactables == null || interactables.Length == 0)
            return $"Hit {hit.collider.gameObject.name} but no AInteractable found";

        var sb = new StringBuilder();
        foreach (var interactable in interactables)
        {
            if (interactable.CanInteract())
            {
                interactable.Interact(im);
                sb.AppendLine($"Interacted: {interactable.GetType().Name} on {interactable.gameObject.name}");
            }
            else
            {
                sb.AppendLine($"Cannot interact: {interactable.GetType().Name} on {interactable.gameObject.name}");
            }
        }

        return sb.Length > 0 ? sb.ToString() : "No interactable responded";
    }

    /// <summary>
    /// List all AInteractable objects within radius.
    /// </summary>
    public static string ListInteractables(float radius = 15f)
    {
        var ctrl = FindLocalController();
        if (ctrl == null) return "ERROR: Player not found";

        Vector3 pos = ctrl.transform.position;
        Vector3 fwd = ctrl.transform.forward;

        var all = Object.FindObjectsByType<AInteractable>(FindObjectsSortMode.None);
        var sb = new StringBuilder();
        int count = 0;

        // Sort by distance
        var sorted = new System.Collections.Generic.List<AInteractable>(all);
        sorted.Sort((a, b) =>
            Vector3.Distance(pos, a.transform.position)
            .CompareTo(Vector3.Distance(pos, b.transform.position)));

        sb.AppendLine($"=== Interactables within {radius}m ===");
        foreach (var inter in sorted)
        {
            float dist = Vector3.Distance(pos, inter.transform.position);
            if (dist > radius) continue;

            Vector3 dir = (inter.transform.position - pos).normalized;
            float angle = Vector3.SignedAngle(fwd, new Vector3(dir.x, 0, dir.z), Vector3.up);
            string compass = AngleToCompass(angle);
            bool canUse = inter.CanInteract();

            sb.AppendLine($"  {dist:F1}m {compass,-8} {inter.GetType().Name} [{inter.gameObject.name}] {(canUse ? "OK" : "locked")}");
            count++;
        }

        if (count == 0) sb.AppendLine("  (none)");
        return sb.ToString();
    }

    private static int GetInteractableLayerMask()
    {
        int mask = 0;
        int itemLayer = LayerMask.NameToLayer("Item");
        int interLayer = LayerMask.NameToLayer("Interactable");
        if (itemLayer >= 0) mask |= 1 << itemLayer;
        if (interLayer >= 0) mask |= 1 << interLayer;
        // Fallback: if neither layer exists, use default
        return mask != 0 ? mask : ~0;
    }

    private static string AngleToCompass(float angle)
    {
        return angle switch
        {
            > -22.5f and <= 22.5f => "front",
            > 22.5f and <= 67.5f => "front-R",
            > 67.5f and <= 112.5f => "right",
            > 112.5f and <= 157.5f => "back-R",
            > 157.5f or <= -157.5f => "back",
            > -157.5f and <= -112.5f => "back-L",
            > -112.5f and <= -67.5f => "left",
            _ => "front-L"
        };
    }

    // ==================== Screenshot ====================

    /// <summary>Capture a screenshot with optional name.</summary>
    public static string Screenshot(string name = null)
    {
        string dir = Application.dataPath + "/../Screenshots";
        if (!System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);

        if (string.IsNullOrEmpty(name))
            name = "capture_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

        string path = dir + "/" + name + ".png";
        ScreenCapture.CaptureScreenshot(path);
        return $"Screenshot: {path}";
    }

    // ==================== Help ====================

    /// <summary>Show available commands.</summary>
    public static string Help()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== DebugRemoteControl ===");
        sb.AppendLine("All commands: unity-cli exec \"return DebugRemoteControl.XXX();\"");
        sb.AppendLine();
        sb.AppendLine("[Movement]");
        sb.AppendLine("  Move(x, y, dur=0.5)   Strafe x(-1~1), forward y(-1~1)");
        sb.AppendLine("  Forward(dur=1)         Walk forward");
        sb.AppendLine("  Back(dur=1)            Walk backward");
        sb.AppendLine("  Left(dur=1)            Strafe left");
        sb.AppendLine("  Right(dur=1)           Strafe right");
        sb.AppendLine("  Stop()                 Stop movement");
        sb.AppendLine("  Sprint(dur=2)          Sprint forward");
        sb.AppendLine("  Jump()                 Jump");
        sb.AppendLine("  Crouch()               Toggle crouch");
        sb.AppendLine();
        sb.AppendLine("[Combat]");
        sb.AppendLine("  Fire()                 Hold trigger");
        sb.AppendLine("  StopFire()             Release trigger");
        sb.AppendLine("  Shoot()                Single shot");
        sb.AppendLine("  Aim(true/false)        Toggle ADS");
        sb.AppendLine("  Reload()               Reload weapon");
        sb.AppendLine("  Grenade()              Throw grenade");
        sb.AppendLine();
        sb.AppendLine("[Camera]");
        sb.AppendLine("  Look(yaw, pitch)       Rotate camera (degrees)");
        sb.AppendLine("  LookRight(deg=30)      Turn right");
        sb.AppendLine("  LookLeft(deg=30)       Turn left");
        sb.AppendLine("  LookUp(deg=15)         Look up");
        sb.AppendLine("  LookDown(deg=15)       Look down");
        sb.AppendLine();
        sb.AppendLine("[Perception]");
        sb.AppendLine("  Scan(dist=50)           Raycast: what am I looking at?");
        sb.AppendLine("  Nearby(radius=15)       All objects within radius");
        sb.AppendLine();
        sb.AppendLine("[Weather/Time]");
        sb.AppendLine("  SetTime(hour)           Set time (0-24, 12=noon)");
        sb.AppendLine("  GetTime()               Current time info");
        sb.AppendLine("  TimeSpeed(speed)        Cycle speed (0=freeze)");
        sb.AppendLine("  Rain(intensity=0.7)     Set rain");
        sb.AppendLine("  Snow(intensity=0.7)     Set snow");
        sb.AppendLine("  ClearWeather()          Stop precipitation");
        sb.AppendLine("  Clouds(amount)          Cloudiness (0-1)");
        sb.AppendLine("  WeatherStatus()         Full weather report");
        sb.AppendLine();
        sb.AppendLine("[Interaction]");
        sb.AppendLine("  Interact()              Use what you're looking at");
        sb.AppendLine("  ListInteractables(r=15) Nearby interactables");
        sb.AppendLine();
        sb.AppendLine("[Utility]");
        sb.AppendLine("  Teleport(x, y, z)      Teleport to position");
        sb.AppendLine("  Pos()                   Quick position check");
        sb.AppendLine("  Status()                Full status report");
        sb.AppendLine("  MultiplayerProbe()      Multiplayer/session/shop snapshot");
        sb.AppendLine("  Screenshot(name)        Capture screenshot");
        sb.AppendLine();
        sb.AppendLine("[Dungeon]");
        sb.AppendLine("  GenerateDungeon()       Generate/regenerate dungeon");
        sb.AppendLine("  BeginWaitForDungeonReady() Begin waiting for dungeon registry ready");
        sb.AppendLine("  GetDungeonReadyResult() Read dungeon-ready wait result");
        sb.AppendLine("  GoToRandomDungeonArea() Teleport to random dungeon area");
        sb.AppendLine("  SpawnMonster(name,cnt)  Spawn monster(s) near player");
        sb.AppendLine("  SpawnItem(name,cnt)     Spawn item(s) near player");
        sb.AppendLine("  ListDungeonEntities()   List nearby dungeon entities");
        sb.AppendLine("  PortalSH(on)            Door-spill SH for player, monsters and items");
        sb.AppendLine("  PortalSHReport()        List probe receivers and their portal SH state");
        sb.AppendLine("  LaunchCurrentClownGiftAtPlayer(speed) Launch live clown gift into player");
        sb.AppendLine("  InspectCurrentClownGift() Show current clown gift rigidbody state");
        sb.AppendLine("  BeginClownGiftImpactTest() Start clown gift impact freeze test");
        sb.AppendLine("  GetClownGiftImpactTestResult() Read clown gift impact freeze test result");
        sb.AppendLine();
        sb.AppendLine("  Help()                  This message");
        return sb.ToString();
    }
}
