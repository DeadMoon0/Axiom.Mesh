namespace Axiom.Mesh.Local;

/// <summary>Who opened a mesh session (LOCAL-SPEC.md §5.8).</summary>
/// <remarks>A value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum SessionDirection : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Accepted by this node.</summary>
    Inbound = 1,

    /// <summary>Dialled by this node.</summary>
    Outbound = 2,
}
