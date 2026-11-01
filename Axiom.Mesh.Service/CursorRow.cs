using Axiom.LDB.Entity;

namespace Axiom.Mesh.Service;

/// <summary>
/// The runtime's one row in a service's database: the <see cref="IndexCursor"/>, written in a transaction of its own after
/// each Entry's handler committed (so a crash between the two repeats an Entry, never loses one).
/// </summary>
[LdbEntity("ServiceCursor")]
internal sealed partial class CursorRow
{
    /// <summary>The row's name; the runtime keeps one, <c>cursor</c>.</summary>
    [Key]
    [Column(MaxLength = 16)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The store epoch, 32 bytes; empty before the first Entry.</summary>
    [Column(MaxLength = 32)]
    public byte[]? Epoch { get; set; }

    /// <summary>The store sequence of the last Entry handled.</summary>
    public ulong LastSeq { get; set; }
}
