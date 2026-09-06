using UnityEngine;

namespace DungeonPortalTransportPoC
{
    /// <summary>
    /// Marker serialized only into the quarantined angle-bake workspace. It lives in
    /// a runtime assembly because Unity cannot reliably deserialize scene components
    /// whose MonoBehaviour type is compiled into an Editor-only assembly.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class DungeonPortalBakedBasisAngleWorkspaceMarker : MonoBehaviour
    {
        [SerializeField] private string roomId;
        [SerializeField] private Quaternion closedLocalRotation = Quaternion.identity;
        [SerializeField] private Vector3 localHingeAxis = Vector3.up;
        [SerializeField] private float openAngleDegrees = 90f;

        public string RoomId => roomId;
        public Quaternion ClosedLocalRotation => closedLocalRotation;
        public Vector3 LocalHingeAxis => localHingeAxis;
        public float OpenAngleDegrees => openAngleDegrees;

        public void Configure(string stableRoomId, Quaternion closed, Vector3 hingeAxis, float angleDegrees)
        {
            roomId = stableRoomId ?? string.Empty;
            closedLocalRotation = closed;
            localHingeAxis = hingeAxis.sqrMagnitude > Mathf.Epsilon ? hingeAxis.normalized : Vector3.up;
            openAngleDegrees = angleDegrees;
        }
    }
}
