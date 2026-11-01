using Axiom.LDB.Entity;

namespace Axiom.Mesh.Chat;

/// <summary>
/// The index's one row about itself (CHAT-SERVICE.md §10): the schema it was built with, the store epoch it was built
/// under, and the store sequence of the last Entry it took in from the Host's stream, written in the same transaction as
/// that Entry's rows.
/// </summary>
[LdbEntity("ChatMeta")]
internal sealed partial class MetaRow
{
    /// <summary>The row's name; the index keeps one, <c>meta</c>.</summary>
    [Key]
    [Column(MaxLength = 16)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The schema version the rows were written with.</summary>
    public int SchemaVersion { get; set; }

    /// <summary>The store epoch, 32 bytes; empty before the first connection.</summary>
    [Column(MaxLength = 32)]
    public byte[]? StoreEpoch { get; set; }

    /// <summary>The store sequence of the last Entry taken in from the stream.</summary>
    public ulong LastSeq { get; set; }
}
