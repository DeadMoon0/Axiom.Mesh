namespace Axiom.Mesh.Protocol;

/// <summary>What a node sends right after a valid <c>Hello</c>, by comparing Roster sequence numbers (SPEC.md §7.1).</summary>
public enum RosterFollowUp
{
    /// <summary>Both sides hold the same Roster of the peer's user: nothing.</summary>
    None = 0,

    /// <summary>The peer holds an older Roster of its own user: send this node's as an unsolicited <c>Object</c>.</summary>
    SendHeldRoster = 1,

    /// <summary>The peer holds a newer Roster of its own user: send <c>RosterQuery</c> for that user.</summary>
    QueryRoster = 2,
}
