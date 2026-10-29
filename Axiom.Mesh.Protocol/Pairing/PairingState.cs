namespace Axiom.Mesh.Protocol;

/// <summary>
/// The states of a pairing, in order (PAIR-SPEC.md §6, LOCAL-SPEC.md §6.2 and §14.10). The values are those of the
/// local protocol's registry.
/// </summary>
public enum PairingState : byte
{
    /// <summary>No pairing.</summary>
    None = 0,

    /// <summary>This side shows its code and waits for the other side, or is connecting with the code it was given.</summary>
    Waiting = 1,

    /// <summary>Both sides are connected and the short authentication string is set; the user compares it.</summary>
    Confirm = 2,

    /// <summary>This side confirmed; the other has not yet.</summary>
    Confirming = 3,

    /// <summary>Both sides confirmed; what the mode exchanges is stored.</summary>
    Paired = 4,

    /// <summary>The pairing failed: expired, refused, broken or disconnected.</summary>
    Failed = 5,

    /// <summary>A user cancelled the pairing, on this side or the other.</summary>
    Cancelled = 6,
}
