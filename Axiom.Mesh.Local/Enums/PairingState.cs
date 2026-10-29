namespace Axiom.Mesh.Local;

/// <summary>The state of a pairing (LOCAL-SPEC.md §6.2, §14.10).</summary>
/// <remarks>A value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum PairingState : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>This side shows its code and waits, or connects with the code it was given.</summary>
    Waiting = 1,

    /// <summary>Both sides are connected and the short authentication string is set.</summary>
    Confirm = 2,

    /// <summary>This side confirmed, the other has not yet.</summary>
    Confirming = 3,

    /// <summary>Done.</summary>
    Paired = 4,

    /// <summary>Ended with a failure, an expiry included.</summary>
    Failed = 5,

    /// <summary>Ended by <c>CancelPairing</c>.</summary>
    Cancelled = 6,
}
