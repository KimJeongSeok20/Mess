using System;
using System.Collections.Generic;
using System.IO;
using DunGen;
using DunGen.Graph;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DungeonRoomLocalLightShare.Editor
{
    public static class RoomLocalMatrixBuilder
    {
        private const string MenuPath =
            "Tools/Dungeon/Room Local Light Share/Build StartMap Room Matrix";
        private const string SourceFlowPath =
            "Assets/Prefabs/map_piece/NewPrison/New_Prison_V2_Flow.asset";
        private const int CensusSeedCount = 512;
        private const float DirectPeakOutlierRatio = 0.25f;

        private readonly struct RoomDefinition
        {
            public readonly string Id;
            public readonly string PrefabPath;

            public RoomDefinition(string id, string prefabPath)
            {
                Id = id;
                PrefabPath = prefabPath;
            }
        }

        private readonly struct AllowedRoomPair
        {
            public readonly int A;
            public readonly int B;

            public AllowedRoomPair(int first, int second)
            {
                A = first;
                B = second;
            }
        }

        private readonly struct ObservedCase : IComparable<ObservedCase>
        {
            public readonly int RoomA;
            public readonly int DoorA;
            public readonly int RoomB;
            public readonly int DoorB;

            public ObservedCase(int roomA, int doorA, int roomB, int doorB)
            {
                RoomA = roomA;
                DoorA = doorA;
                RoomB = roomB;
                DoorB = doorB;
            }

            public string Key => RoomA + ":" + DoorA + "|" + RoomB + ":" + DoorB;

            public int CompareTo(ObservedCase other)
            {
                int value = RoomA.CompareTo(other.RoomA);
                if (value != 0)
                    return value;
                value = RoomB.CompareTo(other.RoomB);
                if (value != 0)
                    return value;
                value = DoorA.CompareTo(other.DoorA);
                return value != 0 ? value : DoorB.CompareTo(other.DoorB);
            }
        }

        private readonly struct CensusResult
        {
            public readonly ObservedCase[] Cases;
            public readonly int SuccessfulSeeds;
            public readonly int LastNewCaseSeed;

            public CensusResult(
                ObservedCase[] cases,
                int successfulSeeds,
                int lastNewCaseSeed)
            {
                Cases = cases;
                SuccessfulSeeds = successfulSeeds;
                LastNewCaseSeed = lastNewCaseSeed;
            }
        }

        private static readonly RoomDefinition[] RoomDefinitions =
        {
            new RoomDefinition(
                "StartRoom",
                "Assets/Prefabs/map_piece/NewPrison/test/V2_StartRoom/StartRoom.prefab"),
            new RoomDefinition(
                "AdminstrativeSegregation",
                "Assets/Prefabs/map_piece/NewPrison/test/V2_AdminstrativeSegregation/" +
                "AdminstrativeSegregation.prefab"),
            new RoomDefinition(
                "Cafeteria",
                "Assets/Prefabs/map_piece/NewPrison/test/V2_Cafeteria/Cafeteria.prefab"),
            new RoomDefinition(
                "OfficerRoom",
                "Assets/Prefabs/map_piece/NewPrison/test/V2_OfficerRoom/OfficerRoom.prefab")
        };

        // Derived from the active New_Prison_V2_Flow: Start uses the dedicated start
        // TileSet; Admin/Cafeteria are multi-door line candidates; Officer is a one-door cap.
        private static readonly AllowedRoomPair[] AllowedPairs =
        {
            new AllowedRoomPair(0, 1),
            new AllowedRoomPair(0, 2),
            new AllowedRoomPair(1, 1),
            new AllowedRoomPair(1, 2),
            new AllowedRoomPair(1, 3),
            new AllowedRoomPair(2, 2),
            new AllowedRoomPair(2, 3)
        };

        [MenuItem(MenuPath)]
        public static void BuildMenu()
        {
            Debug.Log(BuildAll());
        }

        public static string BuildAll()
        {
            string idle = RoomLocalEditorUtil.RequireIdleEditor();
            if (idle != null)
                return idle;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    RoomLocalLightShareContract.MatrixScenePath) == null)
            {
                if (!AssetDatabase.CopyAsset(
                        RoomLocalLightShareContract.MatrixSourceScenePath,
                        RoomLocalLightShareContract.MatrixScenePath))
                    return "FAIL: could not create the isolated matrix scene copy.";
            }

            try
            {
                RoomLocalMatrixCatalog catalog = BuildCatalog();
                if (catalog == null)
                    return "FAIL: matrix catalog generation returned null.";
                string sceneResult = WireMatrixScene(catalog);
                if (sceneResult.StartsWith("FAIL", StringComparison.Ordinal))
                    return sceneResult;
                return
                    "PASS StartMap room matrix\n" +
                    "scene=" + RoomLocalLightShareContract.MatrixScenePath + "\n" +
                    "catalog=" + RoomLocalLightShareContract.MatrixCatalogPath + "\n" +
                    "rooms=" + catalog.Rooms.Length + "\n" +
                    "doorways=" + CountDoorways(catalog) + "\n" +
                    "cases=" + catalog.Cases.Length + "\n" +
                    "sourceSceneUntouched=" + RoomLocalLightShareContract.MatrixSourceScenePath + "\n" +
                    "productionAssetsModified=false\n" +
                    sceneResult;
            }
            catch (Exception exception)
            {
                return "FAIL matrix build: " + exception;
            }
        }

        public static string ValidateCatalogAndScene()
        {
            RoomLocalMatrixCatalog catalog = AssetDatabase.LoadAssetAtPath<RoomLocalMatrixCatalog>(
                RoomLocalLightShareContract.MatrixCatalogPath);
            if (catalog == null)
                return "FAIL: matrix catalog is missing.";
            if (catalog.Rooms.Length < 1)
                return "FAIL: catalog has no room entries.";
            int missingOutgoing = 0;
            for (int room = 0; room < catalog.Rooms.Length; room++)
            {
                RoomLocalMatrixCatalog.RoomEntry entry = catalog.Rooms[room];
                if (entry == null || entry.Prefab == null)
                    return "FAIL: room " + room + " prefab is missing.";
                for (int door = 0; door < entry.Doorways.Length; door++)
                {
                    if (entry.Doorways[door] == null || entry.Doorways[door].Outgoing == null)
                        missingOutgoing++;
                }
            }

            if (missingOutgoing > 0)
                return "FAIL: outgoing map missing on " + missingOutgoing + " doorways.";
            int doorwayCount = CountDoorways(catalog);
            return
                "PASS matrix catalog rooms=" + catalog.Rooms.Length +
                " doorways=" + doorwayCount +
                " outgoing=" + doorwayCount + "/" + doorwayCount +
                " observedCases=" + catalog.Cases.Length;
        }

        public static string CaptureAllFlowOutgoing()
        {
            string idle = RoomLocalEditorUtil.RequireIdleEditor();
            if (idle != null)
                return idle;

            RoomDefinition[] rooms = DiscoverFlowRooms();
            if (rooms.Length == 0)
                return "FAIL: no room prefabs found on " + SourceFlowPath;

            RoomLocalEditorUtil.EnsureFolder(RoomLocalLightShareContract.MatrixDataFolder);
            RoomLocalEditorUtil.EnsureFolder(RoomLocalLightShareContract.MatrixOutgoingFolder);
            Dictionary<string, IncomingBounceData> bounceLookup = BuildBounceLookup();
            var captured = new RoomLocalMatrixCatalog.RoomEntry[rooms.Length];
            for (int i = 0; i < rooms.Length; i++)
                captured[i] = CaptureRoom(rooms[i], bounceLookup);
            CalibratePoweredDirectPeaks(captured);

            RoomLocalMatrixCatalog catalog =
                AssetDatabase.LoadAssetAtPath<RoomLocalMatrixCatalog>(
                    RoomLocalLightShareContract.MatrixCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<RoomLocalMatrixCatalog>();
                AssetDatabase.CreateAsset(catalog, RoomLocalLightShareContract.MatrixCatalogPath);
            }

            catalog.ConfigureAuthoring(
                SourceFlowPath,
                catalog.CensusSeedCount,
                catalog.CensusSuccessCount,
                catalog.LastNewCaseSeed,
                CountDoorwaysFromRooms(captured),
                captured,
                catalog.Cases);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return
                "PASS captured outgoing for every StartMap flow room\n" +
                "rooms=" + captured.Length + "\n" +
                "doorways=" + CountDoorwaysFromRooms(captured) + "\n" +
                "pairBake=false";
        }

        private static RoomLocalMatrixCatalog BuildCatalog()
        {
            RoomLocalEditorUtil.EnsureFolder(RoomLocalLightShareContract.MatrixDataFolder);
            RoomLocalEditorUtil.EnsureFolder(RoomLocalLightShareContract.MatrixOutgoingFolder);
            Dictionary<string, IncomingBounceData> bounceLookup = BuildBounceLookup();
            RoomDefinition[] definitions = DiscoverFlowRooms();
            if (definitions.Length == 0)
                throw new InvalidOperationException("No room prefabs found on " + SourceFlowPath);
            var rooms = new RoomLocalMatrixCatalog.RoomEntry[definitions.Length];
            for (int i = 0; i < definitions.Length; i++)
                rooms[i] = CaptureRoom(definitions[i], bounceLookup);
            CalibratePoweredDirectPeaks(rooms);

            int rawSocketCandidates = CountDoorwaysFromRooms(rooms);
            CensusResult census = ObserveStartMapConnections(rooms, definitions);
            var cases = new List<RoomLocalMatrixCatalog.PairCase>(census.Cases.Length);
            for (int i = 0; i < census.Cases.Length; i++)
            {
                ObservedCase observed = census.Cases[i];
                RoomLocalMatrixCatalog.RoomEntry roomA = rooms[observed.RoomA];
                RoomLocalMatrixCatalog.RoomEntry roomB = rooms[observed.RoomB];
                string id =
                    roomA.RoomId + ":D" + observed.DoorA.ToString("D2") + "-" +
                    roomA.Doorways[observed.DoorA].Kind + " <-> " +
                    roomB.RoomId + ":D" + observed.DoorB.ToString("D2") + "-" +
                    roomB.Doorways[observed.DoorB].Kind;
                cases.Add(new RoomLocalMatrixCatalog.PairCase(
                    id,
                    observed.RoomA,
                    observed.DoorA,
                    observed.RoomB,
                    observed.DoorB));
            }

            RoomLocalMatrixCatalog catalog =
                AssetDatabase.LoadAssetAtPath<RoomLocalMatrixCatalog>(
                    RoomLocalLightShareContract.MatrixCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<RoomLocalMatrixCatalog>();
                AssetDatabase.CreateAsset(catalog, RoomLocalLightShareContract.MatrixCatalogPath);
            }
            catalog.ConfigureAuthoring(
                SourceFlowPath,
                CensusSeedCount,
                census.SuccessfulSeeds,
                census.LastNewCaseSeed,
                rawSocketCandidates,
                rooms,
                cases.ToArray());
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        private static CensusResult ObserveStartMapConnections(
            RoomLocalMatrixCatalog.RoomEntry[] rooms,
            RoomDefinition[] definitions)
        {
            DungeonFlow flow = AssetDatabase.LoadAssetAtPath<DungeonFlow>(SourceFlowPath);
            if (flow == null)
                throw new InvalidOperationException("Missing active StartMap flow " + SourceFlowPath);

            var prefabRoomLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int room = 0; room < definitions.Length; room++)
                prefabRoomLookup[definitions[room].PrefabPath] = room;

            var observed = new Dictionary<string, ObservedCase>(StringComparer.Ordinal);
            int successfulSeeds = 0;
            int lastNewCaseSeed = -1;
            UnityEngine.Object previousSelection = Selection.activeObject;
            Selection.activeObject = null;
            GameObject root = new GameObject("__StartMapMatrixCensus");
            root.hideFlags = HideFlags.HideAndDontSave;
            var generator = new DunGen.DungeonGenerator(root)
            {
                DungeonFlow = flow,
                ShouldRandomizeSeed = false,
                MaxAttemptCount = 20,
                IsAnalysis = true,
                AllowTilePooling = false,
                GenerateAsynchronously = false,
                DebugRender = false,
                LengthMultiplier = 2f,
                TriggerPlacement = DunGen.TriggerPlacementMode.None,
                UpDirection = DunGen.AxisDirection.PosY
            };
            generator.CollisionSettings.OverlapThreshold = 0.01f;
            generator.CollisionSettings.Padding = 0f;
            generator.CollisionSettings.DisallowOverhangs = false;
            generator.CollisionSettings.AvoidCollisionsWithOtherDungeons = false;

            try
            {
                for (int seed = 0; seed < CensusSeedCount; seed++)
                {
                    if ((seed & 31) == 0)
                    {
                        EditorUtility.DisplayProgressBar(
                            "StartMap room connection census",
                            "Deterministic seed " + seed + " / " + CensusSeedCount,
                            seed / (float)CensusSeedCount);
                    }

                    generator.Seed = seed;
                    generator.Generate();
                    if (generator.Status != DunGen.GenerationStatus.Complete ||
                        generator.CurrentDungeon == null)
                    {
                        generator.Clear(true);
                        continue;
                    }

                    successfulSeeds++;
                    IReadOnlyList<DunGen.DoorwayConnection> connections =
                        generator.CurrentDungeon.Connections;
                    for (int connectionIndex = 0;
                         connectionIndex < connections.Count;
                         connectionIndex++)
                    {
                        DunGen.DoorwayConnection connection = connections[connectionIndex];
                        if (!TryResolveObservedEndpoint(
                                connection.A,
                                rooms,
                                prefabRoomLookup,
                                out int roomA,
                                out int doorA) ||
                            !TryResolveObservedEndpoint(
                                connection.B,
                                rooms,
                                prefabRoomLookup,
                                out int roomB,
                                out int doorB))
                            continue;

                        Canonicalize(ref roomA, ref doorA, ref roomB, ref doorB);
                        var candidate = new ObservedCase(roomA, doorA, roomB, doorB);
                        if (!observed.ContainsKey(candidate.Key))
                        {
                            observed.Add(candidate.Key, candidate);
                            lastNewCaseSeed = seed;
                        }
                    }
                    generator.Clear(true);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                generator.Clear(true);
                UnityEngine.Object.DestroyImmediate(root);
                Selection.activeObject = previousSelection;
            }

            var result = new List<ObservedCase>(observed.Values);
            result.Sort();
            return new CensusResult(result.ToArray(), successfulSeeds, lastNewCaseSeed);
        }

        private static bool TryResolveObservedEndpoint(
            Doorway doorway,
            RoomLocalMatrixCatalog.RoomEntry[] rooms,
            Dictionary<string, int> prefabRoomLookup,
            out int roomIndex,
            out int doorwayIndex)
        {
            roomIndex = -1;
            doorwayIndex = -1;
            if (doorway == null || doorway.Tile == null || doorway.Tile.Prefab == null)
                return false;

            string prefabPath = AssetDatabase.GetAssetPath(doorway.Tile.Prefab);
            if (!prefabRoomLookup.TryGetValue(prefabPath, out roomIndex))
                return false;

            string path = AnimationUtility.CalculateTransformPath(
                doorway.transform, doorway.Tile.transform);
            RoomLocalMatrixCatalog.DoorwayEntry[] entries = rooms[roomIndex].Doorways;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].Path == path)
                {
                    doorwayIndex = i;
                    return true;
                }
            }
            return false;
        }

        private static void Canonicalize(
            ref int roomA,
            ref int doorA,
            ref int roomB,
            ref int doorB)
        {
            if (roomA < roomB || (roomA == roomB && doorA <= doorB))
                return;
            (roomA, roomB) = (roomB, roomA);
            (doorA, doorB) = (doorB, doorA);
        }

        private static int CountRawSocketCandidates(
            RoomLocalMatrixCatalog.RoomEntry[] rooms)
        {
            int count = 0;
            for (int pairIndex = 0; pairIndex < AllowedPairs.Length; pairIndex++)
            {
                AllowedRoomPair pair = AllowedPairs[pairIndex];
                count += rooms[pair.A].Doorways.Length * rooms[pair.B].Doorways.Length;
            }
            return count;
        }

        private static void CalibratePoweredDirectPeaks(
            RoomLocalMatrixCatalog.RoomEntry[] rooms)
        {
            var peaks = new List<float>();
            for (int room = 0; room < rooms.Length; room++)
            {
                RoomLocalMatrixCatalog.DoorwayEntry[] doorways = rooms[room].Doorways;
                for (int door = 0; door < doorways.Length; door++)
                {
                    OutgoingPortalMap outgoing = doorways[door].Outgoing;
                    if (outgoing != null && outgoing.Power100Peak > 1e-5f)
                        peaks.Add(outgoing.Power100Peak);
                }
            }
            if (peaks.Count == 0)
                return;

            peaks.Sort();
            float median = peaks[peaks.Count / 2];
            float outlierThreshold = median * DirectPeakOutlierRatio;
            for (int room = 0; room < rooms.Length; room++)
            {
                RoomLocalMatrixCatalog.DoorwayEntry[] doorways = rooms[room].Doorways;
                for (int door = 0; door < doorways.Length; door++)
                {
                    OutgoingPortalMap outgoing = doorways[door].Outgoing;
                    if (outgoing == null)
                        continue;
                    float calibrated = outgoing.Power100Peak < outlierThreshold
                        ? median
                        : outgoing.Power100Peak;
                    outgoing.ConfigureDirectPeakCalibration(calibrated);
                    EditorUtility.SetDirty(outgoing);
                }
            }
        }

        private static int CountCalibratedDirectPeaks(
            RoomLocalMatrixCatalog.RoomEntry[] rooms)
        {
            int count = 0;
            for (int room = 0; room < rooms.Length; room++)
            {
                RoomLocalMatrixCatalog.DoorwayEntry[] doorways = rooms[room].Doorways;
                for (int door = 0; door < doorways.Length; door++)
                {
                    OutgoingPortalMap outgoing = doorways[door].Outgoing;
                    if (outgoing != null && outgoing.HasDirectPeakCalibration)
                        count++;
                }
            }
            return count;
        }

        private static RoomLocalMatrixCatalog.RoomEntry CaptureRoom(
            RoomDefinition definition,
            Dictionary<string, IncomingBounceData> bounceLookup)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(definition.PrefabPath);
            if (prefab == null)
                throw new InvalidOperationException("Missing active V2 prefab " + definition.PrefabPath);

            Scene temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, temp);
                instance.name = definition.Id;
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                DungeonTileRotationSelectorV2 selector =
                    instance.GetComponent<DungeonTileRotationSelectorV2>();
                if (selector != null && !selector.ApplyForCurrentRotation(false))
                    throw new InvalidOperationException(definition.Id + " V2 rotation selection failed.");
                DungeonTileLightmapSwitcher switcher =
                    instance.GetComponent<DungeonTileLightmapSwitcher>();
                if (switcher == null)
                    throw new InvalidOperationException(definition.Id + " has no lightmap switcher.");
                DungeonTileBakeData p0 = switcher.GetBakeData(
                    DungeonTileLightmapSwitcher.PowerLevel.P0);
                DungeonTileBakeData p100 = switcher.GetBakeData(
                    DungeonTileLightmapSwitcher.PowerLevel.P100);
                if (p0 == null || p100 == null)
                    throw new InvalidOperationException(definition.Id + " is missing P0/P100 bake data.");

                Doorway[] doorwayComponents = instance.GetComponentsInChildren<Doorway>(true);
                Array.Sort(doorwayComponents, CompareDoorways);
                var doorwayEntries = new RoomLocalMatrixCatalog.DoorwayEntry[doorwayComponents.Length];
                for (int i = 0; i < doorwayComponents.Length; i++)
                {
                    Doorway doorway = doorwayComponents[i];
                    string path = AnimationUtility.CalculateTransformPath(
                        doorway.transform, instance.transform);
                    string kind = path.IndexOf("Door_LG", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "LG"
                        : "SM";
                    string basePath = RoomLocalLightShareContract.MatrixOutgoingFolder + "/" +
                                      Sanitize(definition.Id) + "_D" + i.ToString("D2") + "_" +
                                      kind + "_Outgoing.asset";
                    OutgoingPortalMap outgoing = CaptureOutgoing(
                        definition.Id,
                        instance.transform,
                        doorway.transform,
                        path,
                        p0,
                        p100,
                        basePath);
                    bounceLookup.TryGetValue(BounceKey(definition.Id, path), out IncomingBounceData bounce);
                    doorwayEntries[i] = new RoomLocalMatrixCatalog.DoorwayEntry(
                        path, kind, outgoing, bounce);
                }
                return new RoomLocalMatrixCatalog.RoomEntry(
                    definition.Id, prefab, doorwayEntries);
            }
            finally
            {
                if (instance != null)
                    UnityEngine.Object.DestroyImmediate(instance);
                if (temp.IsValid() && temp.isLoaded)
                    EditorSceneManager.CloseScene(temp, true);
            }
        }

        private static OutgoingPortalMap CaptureOutgoing(
            string roomId,
            Transform roomRoot,
            Transform doorway,
            string doorwayPath,
            DungeonTileBakeData p0,
            DungeonTileBakeData p100,
            string assetPath)
        {
            Texture2D hdr0 = RoomLocalPortalSampler.Capture(
                roomRoot,
                doorway,
                p0,
                RoomLocalLightShareContract.PortalWidth,
                RoomLocalLightShareContract.PortalHeight);
            Texture2D hdr100 = RoomLocalPortalSampler.Capture(
                roomRoot,
                doorway,
                p100,
                RoomLocalLightShareContract.PortalWidth,
                RoomLocalLightShareContract.PortalHeight);
            if (hdr0 == null || hdr100 == null)
                throw new InvalidOperationException(roomId + " portal sample failed for " + doorwayPath);

            Texture2D cookie0 = null;
            Texture2D cookie100 = null;
            try
            {
                string p0Path = WriteExr(assetPath, "P0", hdr0);
                string p100Path = WriteExr(assetPath, "P100", hdr100);
                cookie0 = RoomLocalPortalSampler.CreateCookie(hdr0, out float peak0);
                cookie100 = RoomLocalPortalSampler.CreateCookie(hdr100, out float peak100);
                string cookie0Path = WritePng(assetPath, "P0_Cookie", cookie0);
                string cookie100Path = WritePng(assetPath, "P100_Cookie", cookie100);

                OutgoingPortalMap map = AssetDatabase.LoadAssetAtPath<OutgoingPortalMap>(assetPath);
                if (map == null)
                {
                    map = ScriptableObject.CreateInstance<OutgoingPortalMap>();
                    AssetDatabase.CreateAsset(map, assetPath);
                }
                map.ConfigureAuthoring(
                    roomId,
                    doorwayPath,
                    AssetDatabase.LoadAssetAtPath<Texture2D>(p0Path),
                    AssetDatabase.LoadAssetAtPath<Texture2D>(p100Path),
                    AssetDatabase.LoadAssetAtPath<Texture2D>(cookie0Path),
                    AssetDatabase.LoadAssetAtPath<Texture2D>(cookie100Path),
                    RoomLocalPortalSampler.Average(hdr0),
                    RoomLocalPortalSampler.Average(hdr100),
                    peak0,
                    peak100);
                EditorUtility.SetDirty(map);
                return map;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hdr0);
                UnityEngine.Object.DestroyImmediate(hdr100);
                if (cookie0 != null)
                    UnityEngine.Object.DestroyImmediate(cookie0);
                if (cookie100 != null)
                    UnityEngine.Object.DestroyImmediate(cookie100);
            }
        }

        private static string WireMatrixScene(RoomLocalMatrixCatalog catalog)
        {
            Scene scene = EditorSceneManager.OpenScene(
                RoomLocalLightShareContract.MatrixScenePath,
                OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                return "FAIL: could not open matrix scene.";

            DestroyRootIfPresent(scene, RoomLocalLightShareContract.StartRoomId);
            DestroyRootIfPresent(scene, RoomLocalLightShareContract.AdministrativeRoomId);
            DestroyRootIfPresent(scene, "RoomLocalLightShare");
            DestroyRootIfPresent(scene, "RoomLocalMatrix");

            Camera camera = FindInScene<Camera>(scene);
            if (camera == null)
                return "FAIL: copied matrix scene has no preview camera.";

            GameObject host = new GameObject("RoomLocalMatrix");
            SceneManager.MoveGameObjectToScene(host, scene);
            RoomLocalMatrixController controller = host.AddComponent<RoomLocalMatrixController>();
            controller.Configure(
                catalog,
                AssetDatabase.LoadAssetAtPath<GameObject>(RoomLocalLightShareContract.DoorPrefabPath),
                AssetDatabase.LoadAssetAtPath<Shader>(RoomLocalLightShareContract.ShaderPath),
                camera);
            host.AddComponent<RoomLocalMatrixHud>();

            RoomLocalEnvironment environment = camera.GetComponent<RoomLocalEnvironment>();
            if (environment != null)
            {
                environment.Configure(
                    FindInScene<DungeonZoneManager>(scene),
                    camera.GetComponent<Volume>(),
                    null,
                    null);
                EditorUtility.SetDirty(environment);
            }

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(host);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(
                    scene, RoomLocalLightShareContract.MatrixScenePath, false))
                return "FAIL: could not save matrix scene.";
            Selection.activeGameObject = host;
            AssetDatabase.SaveAssets();
            return
                "activeSceneLeftOpen=true\ndefaultCase=1/" + catalog.Cases.Length +
                " P0/P100 D050 range=3.8";
        }

        private static Dictionary<string, IncomingBounceData> BuildBounceLookup()
        {
            var lookup = new Dictionary<string, IncomingBounceData>(StringComparer.Ordinal);
            string[] guids = AssetDatabase.FindAssets(
                "t:IncomingBounceData",
                new[] { RoomLocalLightShareContract.BounceFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                IncomingBounceData data = AssetDatabase.LoadAssetAtPath<IncomingBounceData>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (data == null)
                    continue;
                lookup[BounceKey(StripRotation(data.RoomId), data.DoorwayPath)] = data;
            }
            return lookup;
        }

        private static string BounceKey(string roomId, string doorwayPath)
        {
            return StripRotation(roomId) + "\n" + (doorwayPath ?? string.Empty);
        }

        private static string StripRotation(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
                return string.Empty;
            int index = roomId.LastIndexOf("_R", StringComparison.Ordinal);
            return index >= 0 ? roomId.Substring(0, index) : roomId;
        }

        private static int CompareDoorways(Doorway first, Doorway second)
        {
            string a = AnimationUtility.CalculateTransformPath(
                first.transform, first.transform.root);
            string b = AnimationUtility.CalculateTransformPath(
                second.transform, second.transform.root);
            bool aPreferred = a == RoomLocalLightShareContract.PreferredDoorwayPath;
            bool bPreferred = b == RoomLocalLightShareContract.PreferredDoorwayPath;
            if (aPreferred != bPreferred)
                return aPreferred ? -1 : 1;
            return string.CompareOrdinal(a, b);
        }

        private static string WriteExr(string assetPath, string suffix, Texture2D texture)
        {
            string path = Path.ChangeExtension(assetPath, null) + "_" + suffix + ".exr";
            File.WriteAllBytes(path, texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            ImportLinearTexture(path);
            return path;
        }

        private static string WritePng(string assetPath, string suffix, Texture2D texture)
        {
            string path = Path.ChangeExtension(assetPath, null) + "_" + suffix + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            ImportLinearTexture(path);
            return path;
        }

        private static void ImportLinearTexture(string path)
        {
            AssetDatabase.ImportAsset(path);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static string Sanitize(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value.Replace(' ', '_');
        }

        private static int CountDoorways(RoomLocalMatrixCatalog catalog)
        {
            return catalog == null ? 0 : CountDoorwaysFromRooms(catalog.Rooms);
        }

        private static int CountDoorwaysFromRooms(RoomLocalMatrixCatalog.RoomEntry[] rooms)
        {
            int count = 0;
            if (rooms == null)
                return 0;
            for (int i = 0; i < rooms.Length; i++)
                count += rooms[i] != null ? rooms[i].Doorways.Length : 0;
            return count;
        }

        private static RoomDefinition[] DiscoverFlowRooms()
        {
            DungeonFlow flow = AssetDatabase.LoadAssetAtPath<DungeonFlow>(SourceFlowPath);
            if (flow == null)
                return Array.Empty<RoomDefinition>();

            var prefabs = new Dictionary<string, string>(StringComparer.Ordinal);
            if (flow.Nodes != null)
            {
                for (int i = 0; i < flow.Nodes.Count; i++)
                {
                    if (flow.Nodes[i] != null)
                        CollectTileSets(flow.Nodes[i].TileSets, prefabs);
                }
            }

            if (flow.Lines != null)
            {
                for (int i = 0; i < flow.Lines.Count; i++)
                {
                    if (flow.Lines[i] == null || flow.Lines[i].DungeonArchetypes == null)
                        continue;
                    for (int a = 0; a < flow.Lines[i].DungeonArchetypes.Count; a++)
                    {
                        DungeonArchetype archetype = flow.Lines[i].DungeonArchetypes[a];
                        if (archetype == null)
                            continue;
                        CollectTileSets(archetype.TileSets, prefabs);
                        CollectTileSets(archetype.BranchStartTileSets, prefabs);
                        CollectTileSets(archetype.BranchCapTileSets, prefabs);
                    }
                }
            }

            var rooms = new List<RoomDefinition>(prefabs.Count);
            foreach (KeyValuePair<string, string> pair in prefabs)
                rooms.Add(new RoomDefinition(pair.Key, pair.Value));
            rooms.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return rooms.ToArray();
        }

        private static void CollectTileSets(
            List<TileSet> tileSets,
            Dictionary<string, string> prefabs)
        {
            if (tileSets == null)
                return;
            for (int i = 0; i < tileSets.Count; i++)
            {
                TileSet tileSet = tileSets[i];
                if (tileSet == null || tileSet.TileWeights == null || tileSet.TileWeights.Weights == null)
                    continue;
                for (int w = 0; w < tileSet.TileWeights.Weights.Count; w++)
                {
                    GameObject prefab = tileSet.TileWeights.Weights[w] != null
                        ? tileSet.TileWeights.Weights[w].Value
                        : null;
                    if (prefab == null)
                        continue;
                    string path = AssetDatabase.GetAssetPath(prefab);
                    if (string.IsNullOrEmpty(path))
                        continue;
                    prefabs[prefab.name] = path;
                }
            }
        }

        private static void DestroyRootIfPresent(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = roots.Length - 1; i >= 0; i--)
            {
                if (roots[i] != null && roots[i].name == name)
                    UnityEngine.Object.DestroyImmediate(roots[i]);
            }
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T component = roots[i].GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }
            return null;
        }
    }
}
