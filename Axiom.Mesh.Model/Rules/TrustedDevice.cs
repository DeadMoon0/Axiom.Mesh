namespace Axiom.Mesh;

/// <summary>A device as the node's current Rosters state it (SPEC.md §8.8): whose it is, its key, and whether it is removed.</summary>
/// <param name="Id">The device id.</param>
/// <param name="User">The user whose current Roster lists it.</param>
/// <param name="Key">The device key.</param>
/// <param name="IsRemoved">Whether the node treats it as removed (<see cref="RosterRules"/>).</param>
[ClosedShape("A trusted device is the facts of SPEC.md §8.8 about one device.")]
public readonly record struct TrustedDevice(DeviceId Id, UserId User, PublicKey Key, bool IsRemoved);
