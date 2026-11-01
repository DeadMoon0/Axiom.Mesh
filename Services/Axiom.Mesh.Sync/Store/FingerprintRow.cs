using Axiom.LDB.Entity;

namespace Axiom.Mesh.Sync;

/// <summary>
/// What the service knows of one file of a bound directory (SYNC-SERVICE.md §10): keyed by the SHA-256 of the path and the
/// binding, so a path of any length makes a key of 48 bytes.
/// </summary>
[LdbEntity("SyncFingerprints")]
internal sealed partial class FingerprintRow
{
    /// <summary>The SHA-256 of the path, then the binding's id (`RowBytes.Keyed`).</summary>
    [Key]
    [Column(MaxLength = 48)]
    public byte[] Key { get; set; } = [];

    /// <summary>The binding's id.</summary>
    [Index]
    [Column(MaxLength = 16)]
    public byte[] Binding { get; set; } = [];

    /// <summary>The path relative to the directory, with <c>/</c>.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The Tree entry mode it publishes with.</summary>
    public byte Mode { get; set; }

    /// <summary>Its size when read or written.</summary>
    public ulong Size { get; set; }

    /// <summary>Its write time then, in UTC ticks.</summary>
    public long WriteTicks { get; set; }

    /// <summary>When it was read, in UTC ticks; the largest value for a file the service wrote.</summary>
    public long TakenTicks { get; set; }

    /// <summary>Its ChunkList, or the Chunk of a symlink written as a file.</summary>
    [Column(MaxLength = 32)]
    public byte[] Hash { get; set; } = [];
}
