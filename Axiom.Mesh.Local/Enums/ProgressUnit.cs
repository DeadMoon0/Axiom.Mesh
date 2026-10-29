namespace Axiom.Mesh.Local;

/// <summary>What an operation's progress counts (LOCAL-SPEC.md §5.13).</summary>
/// <remarks>Unit 3 is reserved; a value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum ProgressUnit : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Bytes.</summary>
    Bytes = 1,

    /// <summary>Objects.</summary>
    Objects = 2,
}
