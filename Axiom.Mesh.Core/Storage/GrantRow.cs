using Axiom.LDB.Entity;

namespace Axiom.Mesh.Core.Storage;

/// <summary>The current Grant of a Package to one user (DESIGN.md §13 <c>Grants</c>; SPEC.md §8.9). The Grant is an object.</summary>
[LdbEntity("Grants")]
internal sealed partial class GrantRow
{
    /// <summary>The row's key: <see cref="Package"/> then <see cref="Grantee"/> (<see cref="RowKeys.Of(Hash32, Hash32)"/>).</summary>
    [Key]
    [Column(MaxLength = 64)]
    public byte[] Key { get; set; } = [];

    /// <summary>The Package.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Package { get; set; } = [];

    /// <summary>The grantee's user id.</summary>
    [Column(MaxLength = 32)]
    public byte[] Grantee { get; set; } = [];

    /// <summary>The current Grant's hash.</summary>
    [Column(MaxLength = 32)]
    public byte[] Grant { get; set; } = [];

    /// <summary>The current Grant's rights.</summary>
    public GrantRights Rights { get; set; }

    /// <summary>The current Grant's seq.</summary>
    public ulong Seq { get; set; }
}
