namespace Axiom.Mesh.Protocol;

/// <summary>
/// The four modes of a pairing (PAIR-SPEC.md §3, LOCAL-SPEC.md §14.10): the side that shows a code and the side that
/// enters it, for a user or for a device. The values are those of the local protocol's registry.
/// </summary>
public enum PairingMode : byte
{
    /// <summary>No mode; never valid on the wire.</summary>
    None = 0,

    /// <summary>Shows a code; pairs with another user. Needs an identity.</summary>
    ShowUser = 1,

    /// <summary>Enters another user's code. Needs an identity.</summary>
    JoinUser = 2,

    /// <summary>Shows this device's code to be enrolled into a user's Roster. Needs no identity.</summary>
    ShowDevice = 3,

    /// <summary>Enters a device's code and adds it to the own Roster. Needs the user key.</summary>
    AddDevice = 4,
}
