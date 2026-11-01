namespace Axiom.Mesh.Local;

/// <summary>What a planned file is written from (SPEC.md §8.4 <c>mode</c>).</summary>
public enum PlannedFileKind
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>A file: the ChunkList's Chunks in order (mode 1).</summary>
    File = 1,

    /// <summary>An executable file (mode 2): as a file, then marked executable where the file system has the bit.</summary>
    Executable = 2,

    /// <summary>A symlink (mode 4), written as a regular file holding the target text (SPEC.md §11.4 default).</summary>
    Link = 3,
}
