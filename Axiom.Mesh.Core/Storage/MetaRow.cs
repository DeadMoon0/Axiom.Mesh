using Axiom.LDB.Entity;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// One named value about the database itself (DESIGN.md §13 <c>Meta</c>): the store epoch, the last store sequence, the
/// schema version, the store's totals and the own identity. See <c>MetaKeys</c> for the names.
/// </summary>
[LdbEntity("Meta")]
internal sealed partial class MetaRow
{
    /// <summary>The value's name.</summary>
    [Key]
    [Column(MaxLength = 32)]
    public string Name { get; set; } = string.Empty;

    /// <summary>A byte value: an id or a key; <see langword="null"/> for a number.</summary>
    [Column(MaxLength = 65)]
    public byte[]? Bytes { get; set; }

    /// <summary>A number value; 0 for bytes.</summary>
    public ulong Number { get; set; }
}
