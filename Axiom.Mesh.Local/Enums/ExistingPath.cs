namespace Axiom.Mesh.Local;

/// <summary>What <see cref="StoreFiles.MaterialiseAsync"/> does with a directory that exists.</summary>
/// <remarks>A value this enumeration does not name is refused as <c>Invalid</c>.</remarks>
public enum ExistingPath : byte
{
    /// <summary>Assigned to nothing; refused as <c>Invalid</c>.</summary>
    Unassigned = 0,

    /// <summary>Fail with <c>AlreadyExists</c>.</summary>
    Fail = 1,

    /// <summary>Delete the directory first, only one materialised earlier (it holds the marker file), never through a symlink or reparse point.</summary>
    Replace = 2,

    /// <summary>Write into it: files in the Tree replace files of the same name, other files stay.</summary>
    Merge = 3,
}
