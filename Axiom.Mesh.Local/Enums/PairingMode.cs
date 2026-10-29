namespace Axiom.Mesh.Local;

/// <summary>The side a pairing runs on (LOCAL-SPEC.md §6.2, §14.10).</summary>
/// <remarks>A value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum PairingMode : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Shows a code; pairs with another user. Needs an identity.</summary>
    ShowUser = 1,

    /// <summary>Enters the other user's code. Needs an identity.</summary>
    JoinUser = 2,

    /// <summary>Shows this device's code to be enrolled. Needs no identity.</summary>
    ShowDevice = 3,

    /// <summary>Enters a device's code and adds it to the own Roster. Needs the user key.</summary>
    AddDevice = 4,
}
