namespace Axiom.Mesh.Local;

/// <summary>What is at the directory a Tree is to be written to.</summary>
internal enum PathState
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Nothing.</summary>
    Absent = 1,

    /// <summary>A regular file.</summary>
    File = 2,

    /// <summary>A directory without the marker file.</summary>
    Directory = 3,

    /// <summary>A directory holding the marker file: one materialised earlier.</summary>
    MarkedDirectory = 4,

    /// <summary>A symlink or reparse point, never written through.</summary>
    Link = 5,
}
