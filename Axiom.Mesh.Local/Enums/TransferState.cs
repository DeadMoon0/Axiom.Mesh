namespace Axiom.Mesh.Local;

/// <summary>Where an object transfer stands (LOCAL-SPEC.md §5.16).</summary>
/// <remarks>A value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum TransferState : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Wanted.</summary>
    Wanted = 1,

    /// <summary>Received and stored.</summary>
    Stored = 2,

    /// <summary>Sent.</summary>
    Sent = 3,

    /// <summary>Not found.</summary>
    NotFound = 4,

    /// <summary>Cancelled.</summary>
    Cancelled = 5,

    /// <summary>Dropped: the held buffer was full, or the object was faulty.</summary>
    Dropped = 6,
}
