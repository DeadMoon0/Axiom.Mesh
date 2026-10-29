namespace Axiom.Mesh;

/// <summary>What a Tree entry names (SPEC.md §8.4); any other value makes the Tree malformed.</summary>
public enum TreeEntryMode : byte
{
    /// <summary>Mode 0, defined by nothing; never in a well-formed Tree.</summary>
    None = 0,

    /// <summary>A file; the hash names a ChunkList.</summary>
    File = 1,

    /// <summary>An executable file; the hash names a ChunkList.</summary>
    ExecutableFile = 2,

    /// <summary>A directory; the hash names a Tree and the size is 0.</summary>
    Directory = 3,

    /// <summary>A symbolic link; the hash names a Chunk holding the link target.</summary>
    Symlink = 4,
}
