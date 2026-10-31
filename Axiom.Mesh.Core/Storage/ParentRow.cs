using Axiom.LDB.Entity;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// A parent an Entry names that was not stored when the Entry was (SPEC.md §8.6): a history walk stores the newest Entry
/// first. The row is read the other way round when the parent is stored, so the child is checked against its parents once
/// the last of them arrived, and it is removed then.
/// </summary>
[LdbEntity("Parents")]
internal sealed partial class ParentRow
{
    /// <summary>The row's key: <see cref="Child"/> then <see cref="Parent"/> (<see cref="RowKeys.Of(Hash32, Hash32)"/>).</summary>
    [Key]
    [Column(MaxLength = 64)]
    public byte[] Key { get; set; } = [];

    /// <summary>The stored Entry that names the parent.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Child { get; set; } = [];

    /// <summary>The parent, not stored when the child was.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Parent { get; set; } = [];
}
