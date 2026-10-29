using Axiom.LDB.Entity;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// A Package this node follows or a peer lists to it (DESIGN.md §13 <c>Packages</c>; LOCAL-SPEC.md §5.4). The Manifest
/// itself is an object; the row names the current one and keeps what the index needs.
/// </summary>
[LdbEntity("Packages")]
internal sealed partial class PackageRow
{
    /// <summary>The Package id.</summary>
    [Key]
    [Column(MaxLength = 32)]
    public byte[] Package { get; set; } = [];

    /// <summary>Listed or followed.</summary>
    public PackageState State { get; set; }

    /// <summary>The current Manifest's hash; <see langword="null"/> while none is held.</summary>
    [Column(MaxLength = 32)]
    public byte[]? Manifest { get; set; }

    /// <summary>The current Manifest's seq; 0 while none is held.</summary>
    public ulong ManifestSeq { get; set; }

    /// <summary>The local retention policy (LOCAL-SPEC.md §14.14).</summary>
    public LocalRetention Policy { get; set; }

    /// <summary>When the Package was last followed, in milliseconds since the Unix epoch; 0 when never.</summary>
    public long FollowedSinceMs { get; set; }

    /// <summary>The newest stored <c>Version</c>'s hash, which the latest-only policy keeps (SPEC.md §11.2); <see langword="null"/> when none.</summary>
    [Column(MaxLength = 32)]
    public byte[]? NewestVersion { get; set; }

    /// <summary>The newest stored <c>Version</c>'s clock.</summary>
    public ulong NewestVersionClock { get; set; }

    /// <summary>The newest stored <c>Version</c>'s device.</summary>
    [Column(MaxLength = 32)]
    public byte[]? NewestVersionDevice { get; set; }
}
