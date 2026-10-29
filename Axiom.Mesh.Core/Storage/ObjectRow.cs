using Axiom.LDB.Entity;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// A stored object (DESIGN.md §13 <c>Objects</c>): its tag, size, when it was stored and last used, and how many pins
/// hold it. A row exists exactly while the object's file does, except while an eviction waits for a reader
/// (<see cref="EvictPending"/>).
/// </summary>
[LdbEntity("Objects")]
internal sealed partial class ObjectRow
{
    /// <summary>The object's hash.</summary>
    [Key]
    [Column(MaxLength = 32)]
    public byte[] Hash { get; set; } = [];

    /// <summary>The object's tag byte.</summary>
    public byte Tag { get; set; }

    /// <summary>The object's length in bytes.</summary>
    public long Size { get; set; }

    /// <summary>When it was stored, in milliseconds since the Unix epoch.</summary>
    public long StoredMs { get; set; }

    /// <summary>When it was last stored, read or served, in milliseconds since the Unix epoch: the eviction order.</summary>
    [Index]
    public long LastUsedMs { get; set; }

    /// <summary>How many pins hold it; 0 makes it a candidate for eviction (SPEC.md §11.2).</summary>
    public int PinCount { get; set; }

    /// <summary>An eviction found the file held by a reader; it is evicted once the unlink succeeds (LOCAL-SPEC.md §9.7).</summary>
    public bool EvictPending { get; set; }
}
