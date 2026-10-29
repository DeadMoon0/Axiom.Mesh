namespace Axiom.Mesh.Local;

/// <summary>The state of a peer (LOCAL-SPEC.md §5.7, §14.11).</summary>
/// <remarks>A value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum PeerState : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Seen, no session.</summary>
    Discovered = 1,

    /// <summary>A dial is in progress.</summary>
    Dialling = 2,

    /// <summary>A session is established.</summary>
    Established = 3,

    /// <summary>The peer closed before its <c>Hello</c> (SPEC.md §6.2).</summary>
    Refused = 4,

    /// <summary>Not dialled until <c>BackoffUntil</c> (SPEC.md §4.4, §12.1).</summary>
    Backoff = 5,

    /// <summary>Not in any Roster this Host holds; never dialled.</summary>
    Unknown = 6,
}
