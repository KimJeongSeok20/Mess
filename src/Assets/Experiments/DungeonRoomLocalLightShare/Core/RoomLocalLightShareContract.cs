namespace DungeonRoomLocalLightShare
{
    public static class RoomLocalLightShareContract
    {
        public const string OwnedRoot = "Assets/Experiments/DungeonRoomLocalLightShare";
        public const string DataFolder = OwnedRoot + "/Data";
        public const string SceneFolder = OwnedRoot + "/Scenes";
        public const string EvidenceFolder = OwnedRoot + "/Evidence";
        public const string GeneratedFolder = OwnedRoot + "/Generated";
        public const string BounceFolder = GeneratedFolder + "/Bounce";
        public const string OutgoingFolder = DataFolder + "/Outgoing";
        public const string ShaderPath = OwnedRoot + "/Shaders/RoomLocalBounceCompose.shader";
        public const string IsolatedScenePath = SceneFolder + "/Start_Admin_RoomLocalLightShare.unity";
        public const string MatrixSourceScenePath =
            SceneFolder + "/Backups/Start_Admin_RoomLocalLightShare_PreMatrix_20260827_015539.unity";
        public const string MatrixScenePath = SceneFolder + "/StartMap_RoomCombinationMatrix.unity";
        public const string MatrixDataFolder = DataFolder + "/Matrix";
        public const string MatrixOutgoingFolder = MatrixDataFolder + "/Outgoing";
        public const string MatrixCatalogPath = MatrixDataFolder + "/StartMapRoomMatrixCatalog.asset";
        public const string ProductionHashPath = DataFolder + "/production_input_hashes.txt";
        public const string StartMapHookObjectName = "RoomLocalLightShare_StartMapHook";
        public const string StartMapHookPrefabPath =
            OwnedRoot + "/RoomLocalLightShare_StartMapHook.prefab";
        public const string GrokDoorwayStartMapHookObjectName = "GrokDoorwayLighting_StartMapHook";

        public const string StartRoomId = "StartRoom_R000";
        public const string AdministrativeRoomId = "AdminstrativeSegregation_R000";
        public const string PreferredDoorwayPath = "Doorways/Door_SM_A/DoorWayPoint";
        public const string DoorLeafPath = "Door_01";

        public const string StartPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/test/V2_StartRoom/Canonical/StartRoom_R000.prefab";
        public const string AdministrativePrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/test/V2_AdminstrativeSegregation/Canonical/" +
            "AdminstrativeSegregation_R000.prefab";
        public const string StartP0BakePath =
            "Assets/Prefabs/map_piece/NewPrison/test/V2_StartRoom/Canonical/BakedData/" +
            "StartRoom_R000/P0/StartRoom_R000_BakeData.asset";
        public const string StartP100BakePath =
            "Assets/Prefabs/map_piece/NewPrison/test/V2_StartRoom/Canonical/BakedData/" +
            "StartRoom_R000/P100/StartRoom_R000_BakeData.asset";
        public const string AdministrativeP0BakePath =
            "Assets/Prefabs/map_piece/NewPrison/test/V2_AdminstrativeSegregation/Canonical/BakedData/" +
            "AdminstrativeSegregation_R000/P0/AdminstrativeSegregation_R000_BakeData.asset";
        public const string AdministrativeP100BakePath =
            "Assets/Prefabs/map_piece/NewPrison/test/V2_AdminstrativeSegregation/Canonical/BakedData/" +
            "AdminstrativeSegregation_R000/P100/AdminstrativeSegregation_R000_BakeData.asset";
        public const string DoorPrefabPath =
            "Assets/Prefabs/map_piece/NewPrison/SomePicees/Door_SM_A_Door_Placement.prefab";
        public const string LightmapSwitcherPath =
            "Assets/Scripts/Dungeon 1/Lighting/DungeonTileLightmapSwitcher.cs";
        public const string OfficialLightingSettingsPath = "Assets/OfficalLightmap.lighting";
        public const string StartMapScenePath = "Assets/SceneTemplateAssets/Scenes/StartMap.unity";
        public const string PlayerPrefabPath = "Assets/FPS/Cyber_Generic.prefab";
        public const string DroneViewVolumePath =
            "Assets/URP Volume Post-Processing Preset Pack/URP VolumePresets/5_Sci-fi _Future/37_Drone View.asset";

        public const string StartOutgoingPath =
            OutgoingFolder + "/StartRoom_R000_Door_SM_A_Outgoing.asset";
        public const string AdministrativeOutgoingPath =
            OutgoingFolder + "/AdminstrativeSegregation_R000_Door_SM_A_Outgoing.asset";
        public const string StartBouncePath =
            BounceFolder + "/StartRoom_R000/StartRoom_R000_IncomingBounce.asset";
        public const string AdministrativeBouncePath =
            BounceFolder + "/AdminstrativeSegregation_R000/AdminstrativeSegregation_R000_IncomingBounce.asset";

        public const int StartP0IgnoreLightControlCount = 4;
        public const int AdministrativeP0IgnoreLightControlCount = 2;
        public const int DungeonRenderingLayerMask = 2;
        // Dedicated to the hidden door-leaf ShadowsOnly renderer. The doorway Beam combines
        // this with Dungeon (2), allowing the leaf, jamb and connecting walls to block it.
        public const int DoorShadowRenderingLayerMask = 4;
        // Dedicated to visible split-door surfaces. Cookie beams never light this bit.
        // The face also keeps Dungeon so the player's dungeon flashlight can land on P0 doors.
        public const int DoorSurfaceRenderingLayerMask = 8;
        // Visible door faces that have swung into room A may receive A's incoming cookie.
        // They must not receive B's outgoing cookie, which sits in A and fires through the
        // doorway — that is the projector stamp on a half-open leaf.
        public const int DoorReceiveStartRenderingLayerMask = 16;
        public const int DoorReceiveAdministrativeRenderingLayerMask = 32;
        // Floors/walls that should receive doorway cookies. Characters stay on Dungeon
        // only, so a cookie never lights the player even though both share GameObject Default.
        public const int CookieEnvironmentRenderingLayerMask = 128;
        public const int StartIncomingCookieLightingLayerMask =
            CookieEnvironmentRenderingLayerMask | DoorReceiveStartRenderingLayerMask;
        public const int AdministrativeIncomingCookieLightingLayerMask =
            CookieEnvironmentRenderingLayerMask | DoorReceiveAdministrativeRenderingLayerMask;
        public const int DoorExclusiveRenderingLayerMask =
            DoorShadowRenderingLayerMask |
            DoorSurfaceRenderingLayerMask |
            DoorReceiveStartRenderingLayerMask |
            DoorReceiveAdministrativeRenderingLayerMask |
            PortalShutterRenderingLayerMask;
        public const int DoorwayShadowRenderingLayerMask =
            DungeonRenderingLayerMask | DoorShadowRenderingLayerMask;
        // Quad in the doorway plane that represents the still-blocked socket. Cookie
        // shadows use this instead of the swung 3D leaf, so an inward-open door cannot
        // sit in front of the projector and also so the projector can stay far enough
        // to light the receiver floor at the jamb.
        public const int PortalShutterRenderingLayerMask = 64;
        public const int CookieShadowRenderingLayerMask =
            DungeonRenderingLayerMask | PortalShutterRenderingLayerMask;
        public const int DungeonCullingMask = 66177;
        public const int PortalWidth = 64;
        public const int PortalHeight = 128;

        public static readonly float[] AuthoredOpenFractions = { 0.5f, 1f };

        public static int ExpectedIgnoreLightControlCount(string roomId)
        {
            if (roomId == StartRoomId)
                return StartP0IgnoreLightControlCount;
            if (roomId == AdministrativeRoomId)
                return AdministrativeP0IgnoreLightControlCount;
            return -1;
        }
    }
}
