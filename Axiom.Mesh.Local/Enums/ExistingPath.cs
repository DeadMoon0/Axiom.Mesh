namespace Axiom.Mesh.Local;

/// <summary>What <c>Materialise</c> does with a path that exists (LOCAL-SPEC.md §6.4).</summary>
/// <remarks>A value this enumeration does not name is answered <c>Invalid</c> by the Host.</remarks>
public enum ExistingPath : byte
{
    /// <summary>Assigned to nothing; answered <c>Invalid</c>.</summary>
    Unassigned = 0,

    /// <summary>Fail the operation with <c>AlreadyExists</c>.</summary>
    Fail = 1,

    /// <summary>Delete the path first, only a directory this Host materialised or a regular file.</summary>
    Replace = 2,

    /// <summary>Write into it: files in the Tree replace files of the same name, other files stay.</summary>
    Merge = 3,
}
