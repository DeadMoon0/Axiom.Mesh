namespace Axiom.Mesh.Local;

/// <summary>What happened in a <c>PeerChanged</c> event, the peer change kinds of LOCAL-SPEC.md §14.11.</summary>
/// <remarks>Values 8 to 255 are reserved; a value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum PeerChange : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Discovery saw the device.</summary>
    Discovered = 1,

    /// <summary>A dial started.</summary>
    Dialling = 2,

    /// <summary>A session was established; <c>Session</c> is present.</summary>
    Established = 3,

    /// <summary>A session ended; <c>Session</c> is present.</summary>
    Closed = 4,

    /// <summary>The peer closed before its <c>Hello</c> (SPEC.md §6.2).</summary>
    RefusedByPeer = 5,

    /// <summary>This node refused the device (TLS check or admission).</summary>
    RefusedHere = 6,

    /// <summary>Its user was forgotten (§6.2).</summary>
    Forgotten = 7,
}
