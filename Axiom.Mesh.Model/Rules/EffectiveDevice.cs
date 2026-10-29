namespace Axiom.Mesh;

/// <summary>A device of a current Roster as the node treats it (SPEC.md §8.8).</summary>
/// <param name="Device">The device as the Roster lists it.</param>
/// <param name="Removal">Why it counts as removed, or <see cref="DeviceRemoval.None"/>.</param>
[ClosedShape("A device's effective state is its listing and its removal (SPEC.md §8.8).")]
public readonly record struct EffectiveDevice(RosterDevice Device, DeviceRemoval Removal)
{
    /// <summary>Whether the node treats the device as removed: closed, refused, and its unstored Entries invalid.</summary>
    public bool IsRemoved => Removal != DeviceRemoval.None;
}
