using Axiom.LDB.Entity;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// One reference from a stored object to another (SPEC.md §9.7, §11.2): an Entry's payload to its Tree or attachments, a
/// Tree to its entries, a ChunkList to its Chunks. Read upwards to find the Packages an object is visible and pinned
/// through, downwards to pin a closure; the target need not be stored.
/// </summary>
[LdbEntity("References")]
internal sealed partial class ReferenceRow
{
    /// <summary>The row's key: <see cref="Referrer"/> then <see cref="Target"/> (<see cref="RowKeys.Of(Hash32, Hash32)"/>).</summary>
    [Key]
    [Column(MaxLength = 64)]
    public byte[] Key { get; set; } = [];

    /// <summary>The referencing object.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Referrer { get; set; } = [];

    /// <summary>The referenced object.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Target { get; set; } = [];
}
