namespace Axiom.Mesh.Local;

/// <summary>What happened in a <c>UserChanged</c> event (LOCAL-SPEC.md §7.3).</summary>
/// <remarks>A value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum UserChange : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>A user became known.</summary>
    Known = 1,

    /// <summary>A new current Roster is held for a known user.</summary>
    Roster = 2,

    /// <summary>A user was forgotten.</summary>
    Forgotten = 3,
}
