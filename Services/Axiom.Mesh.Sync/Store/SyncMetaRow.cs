using Axiom.LDB.Entity;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The database's one row about itself (SYNC-SERVICE.md §10): the schema it was built with, the store epoch its Version DAG was
/// built under, the store sequence of the last Entry the stream delivered, and the number the next conflict gets.
/// </summary>
[LdbEntity("SyncMeta")]
internal sealed partial class SyncMetaRow
{
    /// <summary>The row's name; the database keeps one, <c>meta</c>.</summary>
    [Key]
    [Column(MaxLength = 16)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The schema version the rows were written with.</summary>
    public int SchemaVersion { get; set; }

    /// <summary>The store epoch, 32 bytes; empty before the first connection.</summary>
    [Column(MaxLength = 32)]
    public byte[]? StoreEpoch { get; set; }

    /// <summary>The store sequence of the last Version the stream delivered.</summary>
    public ulong LastSeq { get; set; }

    /// <summary>The number the next conflict gets.</summary>
    public ulong NextConflict { get; set; }
}
