using UnityEngine;

[DisallowMultipleComponent]
public sealed class DungeonDoorProbeRendererGroup : MonoBehaviour
{
    public enum Group
    {
        PositiveZ,
        NegativeZ,
        Edge
    }

    [SerializeField] private Group probeGroup = Group.Edge;

    public Group ProbeGroup => probeGroup;

    public void Configure(Group group)
    {
        probeGroup = group;
    }
}
