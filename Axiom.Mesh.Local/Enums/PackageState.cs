namespace Axiom.Mesh.Local;

/// <summary>Whether this node follows a Package (LOCAL-SPEC.md §5.4).</summary>
/// <remarks>A value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum PackageState : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>A peer lists it to this node; it is not followed.</summary>
    Listed = 1,

    /// <summary>This node follows it.</summary>
    Followed = 2,
}
