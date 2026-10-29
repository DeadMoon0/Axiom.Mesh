using Axiom.LDB.Entity;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// A device this node saw or connected to (DESIGN.md §13 <c>Peers</c>; LOCAL-SPEC.md §5.7): where it was last reached,
/// and the backoff and violations SPEC.md §4.4 and §12.1 keep against it.
/// </summary>
[LdbEntity("Peers")]
internal sealed partial class PeerRow
{
    /// <summary>The device id.</summary>
    [Key]
    [Column(MaxLength = 32)]
    public byte[] Device { get; set; } = [];

    /// <summary>The device's user, as proven by a session; <see langword="null"/> while unknown.</summary>
    [Column(MaxLength = 32)]
    public byte[]? User { get; set; }

    /// <summary>The most recent addresses, <c>host:port</c>, newline-separated.</summary>
    public string Addresses { get; set; } = string.Empty;

    /// <summary>First seen, in milliseconds since the Unix epoch.</summary>
    public long FirstSeenMs { get; set; }

    /// <summary>Last seen, in milliseconds since the Unix epoch.</summary>
    public long LastSeenMs { get; set; }

    /// <summary>Not dialled before this time, in milliseconds since the Unix epoch; 0 when not backing off.</summary>
    public long BackoffUntilMs { get; set; }

    /// <summary>Violations recorded since <see cref="ViolationsSinceMs"/>.</summary>
    public int Violations { get; set; }

    /// <summary>The start of the window <see cref="Violations"/> counts, in milliseconds since the Unix epoch.</summary>
    public long ViolationsSinceMs { get; set; }
}
