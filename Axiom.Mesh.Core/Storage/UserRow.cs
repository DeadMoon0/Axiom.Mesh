using Axiom.LDB.Entity;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// A user this node holds something of (DESIGN.md §13 <c>Users</c>): paired users and the own user, and every user whose
/// current Roster the node stores. Users known through a followed Package's Manifest or Grant are derived, not stored.
/// </summary>
[LdbEntity("Users")]
internal sealed partial class UserRow
{
    /// <summary>The user id, <c>H(UserKey)</c>.</summary>
    [Key]
    [Column(MaxLength = 32)]
    public byte[] User { get; set; } = [];

    /// <summary>The user key's 65-byte encoding.</summary>
    [Column(MaxLength = 65)]
    public byte[] UserKey { get; set; } = [];

    /// <summary>Known through pairing, or the own user (SPEC.md §1.2): known whatever Packages say.</summary>
    public bool Paired { get; set; }

    /// <summary>The current Roster's hash; <see langword="null"/> while none is stored.</summary>
    [Column(MaxLength = 32)]
    public byte[]? Roster { get; set; }

    /// <summary>The current Roster's seq; 0 while none is stored.</summary>
    public ulong RosterSeq { get; set; }

    /// <summary>When the row was made, in milliseconds since the Unix epoch.</summary>
    public long SinceMs { get; set; }
}
