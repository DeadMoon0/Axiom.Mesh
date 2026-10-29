namespace Axiom.Mesh.Local;

/// <summary>Which way an object travels (LOCAL-SPEC.md §5.16).</summary>
/// <remarks>A value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum TransferDirection : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>This node wants or received it.</summary>
    In = 1,

    /// <summary>The peer wants it or was sent it.</summary>
    Out = 2,
}
