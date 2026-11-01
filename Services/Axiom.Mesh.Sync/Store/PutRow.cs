using Axiom.LDB.Entity;

namespace Axiom.Mesh.Sync;

/// <summary>
/// An object a run put (LOCAL-SPEC.md §9.5), whose <c>Service</c> pin this service still holds: unpinned at the end of the run,
/// or of the next run when the service stopped first.
/// </summary>
[LdbEntity("SyncPuts")]
internal sealed partial class PutRow
{
    /// <summary>The object's hash, then the binding's id (`RowBytes.Keyed`).</summary>
    [Key]
    [Column(MaxLength = 48)]
    public byte[] Key { get; set; } = [];

    /// <summary>The binding's id.</summary>
    [Index]
    [Column(MaxLength = 16)]
    public byte[] Binding { get; set; } = [];

    /// <summary>The object.</summary>
    [Column(MaxLength = 32)]
    public byte[] Hash { get; set; } = [];
}
