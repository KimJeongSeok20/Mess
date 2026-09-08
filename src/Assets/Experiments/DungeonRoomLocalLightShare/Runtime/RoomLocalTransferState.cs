namespace DungeonRoomLocalLightShare
{
    public enum RoomLocalPortalBinding
    {
        Unresolved,
        Door,
        OpenPassage
    }

    /// <summary>One connection's physical aperture and power state, shared by lighting and probes.</summary>
    public readonly struct RoomLocalTransferState
    {
        public readonly bool Enabled;
        public readonly RoomLocalPortalBinding Binding;
        public readonly float OpenFraction;
        public readonly float ApertureFraction;
        public readonly float StartPower01;
        public readonly float AdministrativePower01;

        public RoomLocalTransferState(bool enabled, RoomLocalPortalBinding binding,
            float openFraction, float apertureFraction, float startPower01, float administrativePower01)
        {
            Enabled = enabled;
            Binding = binding;
            OpenFraction = openFraction;
            ApertureFraction = apertureFraction;
            StartPower01 = startPower01;
            AdministrativePower01 = administrativePower01;
        }
    }
}
