namespace Axiom.Mesh.Local;

/// <summary>The state of a mesh session (LOCAL-SPEC.md §5.8, SPEC.md §6.1).</summary>
/// <remarks>A value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum SessionState : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>The connection exists; <c>Hello</c> not yet exchanged.</summary>
    Connected = 1,

    /// <summary>Both <c>Hello</c>s were accepted.</summary>
    Established = 2,

    /// <summary>The session carries a service protocol.</summary>
    Switched = 3,
}
