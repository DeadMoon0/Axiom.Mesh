namespace Axiom.Mesh.Local;

/// <summary>What <see cref="StoreFiles.MaterialiseFileAsync"/> wrote: one file, from its ChunkList.</summary>
public sealed record MaterialisedFile
{
    /// <summary>The ChunkList written.</summary>
    public required ObjectHash ChunkList { get; init; }

    /// <summary>The file's bytes, as the ChunkList states and the Chunks add up to (SPEC.md §11.4).</summary>
    public required ulong Bytes { get; init; }
}
