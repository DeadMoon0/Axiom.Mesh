using Axiom.LDB.Entity;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// A device's Head in a Package (DESIGN.md §13 <c>Heads</c>; SPEC.md §9.3): of the Entries the device wrote that are
/// stored here, the one with the highest clock, ties to the lower hash.
/// </summary>
[LdbEntity("Heads")]
internal sealed partial class HeadRow
{
    /// <summary>The row's key: <see cref="Package"/> then <see cref="Device"/> (<see cref="RowKeys.Of(Hash32, Hash32)"/>).</summary>
    [Key]
    [Column(MaxLength = 64)]
    public byte[] Key { get; set; } = [];

    /// <summary>The Package.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Package { get; set; } = [];

    /// <summary>The writing device.</summary>
    [Column(MaxLength = 32)]
    public byte[] Device { get; set; } = [];

    /// <summary>The Head Entry's hash.</summary>
    [Column(MaxLength = 32)]
    public byte[] Entry { get; set; } = [];

    /// <summary>The Head Entry's clock.</summary>
    public ulong Clock { get; set; }
}
