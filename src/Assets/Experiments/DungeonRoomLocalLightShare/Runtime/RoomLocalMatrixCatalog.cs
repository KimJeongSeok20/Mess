using System;
using UnityEngine;

namespace DungeonRoomLocalLightShare
{
    [CreateAssetMenu(
        fileName = "RoomLocalMatrixCatalog",
        menuName = "Dungeon/Room Local Light Share/Matrix Catalog")]
    public sealed class RoomLocalMatrixCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class DoorwayEntry
        {
            [SerializeField] private string path;
            [SerializeField] private string kind;
            [SerializeField] private OutgoingPortalMap outgoing;
            [SerializeField] private IncomingBounceData incomingBounce;

            public string Path => path;
            public string Kind => kind;
            public OutgoingPortalMap Outgoing => outgoing;
            public IncomingBounceData IncomingBounce => incomingBounce;

            public DoorwayEntry(
                string stablePath,
                string doorwayKind,
                OutgoingPortalMap outgoingMap,
                IncomingBounceData bounceData)
            {
                path = stablePath ?? string.Empty;
                kind = doorwayKind ?? string.Empty;
                outgoing = outgoingMap;
                incomingBounce = bounceData;
            }
        }

        [Serializable]
        public sealed class RoomEntry
        {
            [SerializeField] private string roomId;
            [SerializeField] private GameObject prefab;
            [SerializeField] private DoorwayEntry[] doorways = Array.Empty<DoorwayEntry>();

            public string RoomId => roomId;
            public GameObject Prefab => prefab;
            public DoorwayEntry[] Doorways => doorways ?? Array.Empty<DoorwayEntry>();

            public RoomEntry(string stableRoomId, GameObject roomPrefab, DoorwayEntry[] entries)
            {
                roomId = stableRoomId ?? string.Empty;
                prefab = roomPrefab;
                doorways = entries ?? Array.Empty<DoorwayEntry>();
            }
        }

        [Serializable]
        public sealed class PairCase
        {
            [SerializeField] private string caseId;
            [SerializeField] private int roomA;
            [SerializeField] private int doorwayA;
            [SerializeField] private int roomB;
            [SerializeField] private int doorwayB;

            public string CaseId => caseId;
            public int RoomA => roomA;
            public int DoorwayA => doorwayA;
            public int RoomB => roomB;
            public int DoorwayB => doorwayB;

            public PairCase(string id, int firstRoom, int firstDoor, int secondRoom, int secondDoor)
            {
                caseId = id ?? string.Empty;
                roomA = firstRoom;
                doorwayA = firstDoor;
                roomB = secondRoom;
                doorwayB = secondDoor;
            }
        }

        [SerializeField] private string sourceFlowPath;
        [SerializeField] private int censusSeedCount;
        [SerializeField] private int censusSuccessCount;
        [SerializeField] private int lastNewCaseSeed = -1;
        [SerializeField] private int rawSocketCandidateCount;
        [SerializeField] private RoomEntry[] rooms = Array.Empty<RoomEntry>();
        [SerializeField] private PairCase[] cases = Array.Empty<PairCase>();

        public string SourceFlowPath => sourceFlowPath;
        public int CensusSeedCount => censusSeedCount;
        public int CensusSuccessCount => censusSuccessCount;
        public int LastNewCaseSeed => lastNewCaseSeed;
        public int RawSocketCandidateCount => rawSocketCandidateCount;
        public RoomEntry[] Rooms => rooms ?? Array.Empty<RoomEntry>();
        public PairCase[] Cases => cases ?? Array.Empty<PairCase>();

        public void ConfigureAuthoring(
            string flowPath,
            int scannedSeeds,
            int successfulSeeds,
            int lastSeedWithNewCase,
            int unfilteredSocketCandidates,
            RoomEntry[] roomEntries,
            PairCase[] pairCases)
        {
            sourceFlowPath = flowPath ?? string.Empty;
            censusSeedCount = Mathf.Max(0, scannedSeeds);
            censusSuccessCount = Mathf.Max(0, successfulSeeds);
            lastNewCaseSeed = lastSeedWithNewCase;
            rawSocketCandidateCount = Mathf.Max(0, unfilteredSocketCandidates);
            rooms = roomEntries ?? Array.Empty<RoomEntry>();
            cases = pairCases ?? Array.Empty<PairCase>();
        }

        public bool TryResolve(
            int caseIndex,
            out PairCase pair,
            out RoomEntry roomA,
            out DoorwayEntry doorwayA,
            out RoomEntry roomB,
            out DoorwayEntry doorwayB,
            out string failure)
        {
            pair = null;
            roomA = null;
            doorwayA = null;
            roomB = null;
            doorwayB = null;
            PairCase[] authoredCases = Cases;
            RoomEntry[] authoredRooms = Rooms;
            if (caseIndex < 0 || caseIndex >= authoredCases.Length)
            {
                failure = "Matrix case index is outside the catalog.";
                return false;
            }

            pair = authoredCases[caseIndex];
            if (pair == null || pair.RoomA < 0 || pair.RoomA >= authoredRooms.Length ||
                pair.RoomB < 0 || pair.RoomB >= authoredRooms.Length)
            {
                failure = "Matrix case has an invalid room index.";
                return false;
            }

            roomA = authoredRooms[pair.RoomA];
            roomB = authoredRooms[pair.RoomB];
            if (roomA == null || roomB == null || roomA.Prefab == null || roomB.Prefab == null ||
                pair.DoorwayA < 0 || pair.DoorwayA >= roomA.Doorways.Length ||
                pair.DoorwayB < 0 || pair.DoorwayB >= roomB.Doorways.Length)
            {
                failure = "Matrix case has a missing prefab or doorway.";
                return false;
            }

            doorwayA = roomA.Doorways[pair.DoorwayA];
            doorwayB = roomB.Doorways[pair.DoorwayB];
            if (doorwayA == null || doorwayB == null)
            {
                failure = "Matrix case doorway entry is missing.";
                return false;
            }

            failure = null;
            return true;
        }

        public bool TryFindDoorway(
            string liveRoomId,
            string liveDoorwayPath,
            out RoomEntry room,
            out DoorwayEntry doorway)
        {
            room = null;
            doorway = null;
            RoomEntry[] authoredRooms = Rooms;
            for (int i = 0; i < authoredRooms.Length; i++)
            {
                RoomEntry candidate = authoredRooms[i];
                if (candidate == null ||
                    !RoomLocalEndpointKeys.RoomIdsMatch(liveRoomId, candidate.RoomId))
                    continue;

                DoorwayEntry[] entries = candidate.Doorways;
                for (int door = 0; door < entries.Length; door++)
                {
                    DoorwayEntry entry = entries[door];
                    if (entry == null ||
                        !RoomLocalEndpointKeys.PathsMatch(liveDoorwayPath, entry.Path))
                        continue;

                    room = candidate;
                    doorway = entry;
                    return true;
                }
            }

            return false;
        }
    }
}
