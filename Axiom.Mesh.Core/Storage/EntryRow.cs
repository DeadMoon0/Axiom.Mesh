using Axiom.LDB.Entity;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// A stored Entry, the index of it (DESIGN.md §13 <c>Entries</c>; LOCAL-SPEC.md §5.6). The bytes are an object. The store
/// sequence is unique and never reused: an Entry evicted and stored again gets a new one.
/// </summary>
/// <remarks>
/// The total order (clock, device, hash) is read through the ordered index on <see cref="Clock"/>, and ties of one clock
/// are put in order in memory: LDB 0.1.0 streams an order only through a one-column index of a numeric type, and
/// sorts every other order, a byte-string column included, in memory (see <c>EntryIndex</c>).
/// </remarks>
[LdbEntity("Entries")]
internal sealed partial class EntryRow
{
    /// <summary>The Entry's hash.</summary>
    [Key]
    [Column(MaxLength = 32)]
    public byte[] Hash { get; set; } = [];

    /// <summary>The store sequence: 1 for the first Entry this node ever stored, then one more for each (LOCAL-SPEC.md §5.6).</summary>
    [Index(Unique = true)]
    public ulong StoreSeq { get; set; }

    /// <summary>The Package.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Package { get; set; } = [];

    /// <summary>The writing device.</summary>
    [Column(MaxLength = 32)]
    public byte[] Device { get; set; } = [];

    /// <summary>The Lamport clock.</summary>
    [Index]
    public ulong Clock { get; set; }

    /// <summary>The parents' hashes, ascending, concatenated: 0, 32 or 64 bytes.</summary>
    [Column(MaxLength = 64)]
    public byte[] Parents { get; set; } = [];

    /// <summary>The Entry kind.</summary>
    public EntryKind Kind { get; set; }

    /// <summary>The Entry its envelope's <c>redacts</c> names (SPEC.md §8.6, §9.4); <see langword="null"/> when it is no redaction.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[]? Target { get; set; }

    /// <summary>A stored redaction that SPEC.md §9.4 honours names this Entry: its payload is never shown.</summary>
    public bool Redacted { get; set; }

    /// <summary>When this node stored it, in milliseconds since the Unix epoch.</summary>
    public long StoredMs { get; set; }
}
