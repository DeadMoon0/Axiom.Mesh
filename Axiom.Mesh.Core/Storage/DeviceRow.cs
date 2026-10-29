using Axiom.LDB.Entity;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// A device some Roster of a user listed (DESIGN.md §13 <c>Devices</c>). Keyed by user and device, because a device id
/// can stand in the Rosters of two users, of which the later one counts it removed (SPEC.md §8.8). Rows of devices a
/// later Roster omitted stay, with <see cref="RemovedEver"/> set: removal is permanent.
/// </summary>
[LdbEntity("Devices")]
internal sealed partial class DeviceRow
{
    /// <summary>The row's key: <see cref="User"/> then <see cref="Device"/> (<see cref="RowKeys.Of(Hash32, Hash32)"/>).</summary>
    [Key]
    [Column(MaxLength = 64)]
    public byte[] Key { get; set; } = [];

    /// <summary>The user whose Roster lists the device.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] User { get; set; } = [];

    /// <summary>The device id, <c>H(DeviceKey)</c>.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Device { get; set; } = [];

    /// <summary>The device key's 65-byte encoding.</summary>
    [Column(MaxLength = 65)]
    public byte[] DeviceKey { get; set; } = [];

    /// <summary>The device's name, as the latest Roster that listed it says.</summary>
    [Column(MaxLength = 64)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether the user's current Roster lists the device.</summary>
    public bool Listed { get; set; }

    /// <summary>Why the node treats the device as removed in the current Roster, or <see cref="DeviceRemoval.None"/>.</summary>
    public DeviceRemoval Removal { get; set; }

    /// <summary>Removed or omitted in any Roster of the user the node verified: removed in every later one (SPEC.md §8.8).</summary>
    public bool RemovedEver { get; set; }
}
