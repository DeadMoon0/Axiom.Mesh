namespace Axiom.Mesh.Local;

/// <summary>What an operation does (LOCAL-SPEC.md §8.6, §14.6).</summary>
/// <remarks>Values 4 to 255 are reserved; a value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum OperationKind : ushort
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Writes a Tree or a ChunkList to the file system (<c>Materialise</c>).</summary>
    Materialise = 1,

    /// <summary>Fetches objects from peers (<c>Fetch</c>).</summary>
    Fetch = 2,

    /// <summary>Chunks and stores a file (<c>PutFile</c>).</summary>
    PutFile = 3,
}
