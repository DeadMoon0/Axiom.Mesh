using Axiom.LDB.Entity;

namespace Axiom.Mesh.Core.Storage;

/// <summary>A reason to keep a stored object (DESIGN.md §13 <c>Pins</c>; SPEC.md §11.2; LOCAL-SPEC.md §5.11).</summary>
[LdbEntity("Pins")]
internal sealed partial class PinRow
{
    /// <summary>The row's key: hash, reason byte, source (<see cref="RowKeys.Pin"/>), the order of LOCAL-SPEC.md §5.11.</summary>
    [Key]
    [Column(MaxLength = 65)]
    public byte[] Key { get; set; } = [];

    /// <summary>The pinned object.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Hash { get; set; } = [];

    /// <summary>Why.</summary>
    public PinReason Reason { get; set; }

    /// <summary>Who for (LOCAL-SPEC.md §5.11): a Package id, a user id, all zero, or <c>H(service name)</c>.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Source { get; set; } = [];

    /// <summary>When it was pinned, in milliseconds since the Unix epoch.</summary>
    public long SinceMs { get; set; }
}
