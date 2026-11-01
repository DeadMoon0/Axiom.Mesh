using Axiom.LDB.Entity;

namespace Axiom.Mesh.Sync;

/// <summary>
/// One binding (SYNC-SERVICE.md §10): the directory, the Archive and the branch, the base the directory last equalled with the
/// leaves it was taken from, the target a run is writing (set before the disk changes, cleared with the base after), and what the
/// last run found. Every write of the base is durable.
/// </summary>
[LdbEntity("SyncBindings")]
internal sealed partial class BindingRow
{
    /// <summary>The binding's id: 16 bytes derived from its directory and Archive, also in the marker file.</summary>
    [Key]
    [Column(MaxLength = 16)]
    public byte[] Id { get; set; } = [];

    /// <summary>The bound directory, absolute.</summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>The Archive.</summary>
    [Column(MaxLength = 32)]
    public byte[] Package { get; set; } = [];

    /// <summary>The branch.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The <see cref="SymlinkPolicy"/>.</summary>
    public byte Symlinks { get; set; }

    /// <summary>The ignore patterns, one per line.</summary>
    public string Ignore { get; set; } = string.Empty;

    /// <summary>The Tree the directory last equalled; <see langword="null"/> before the first run.</summary>
    [Column(MaxLength = 32)]
    public byte[]? BaseTree { get; set; }

    /// <summary>The leaves the base was taken from, 32 bytes each.</summary>
    public byte[] BaseLeaves { get; set; } = [];

    /// <summary>The Tree a run is writing into the directory; <see langword="null"/> when none is.</summary>
    [Column(MaxLength = 32)]
    public byte[]? PendingTree { get; set; }

    /// <summary>The leaves the pending Tree comes from, 32 bytes each.</summary>
    public byte[] PendingLeaves { get; set; } = [];

    /// <summary>The <see cref="BindingState"/> the last run ended in.</summary>
    public byte State { get; set; }

    /// <summary>Why the binding is in error or waiting; empty otherwise.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>When the last run ended, in milliseconds since the Unix epoch; 0 when none did.</summary>
    public long LastRunMs { get; set; }

    /// <summary>How many runs ended.</summary>
    public ulong Runs { get; set; }

    /// <summary>The files of the directory at the last run.</summary>
    public ulong Files { get; set; }

    /// <summary>Their bytes.</summary>
    public ulong Bytes { get; set; }

    /// <summary>Changes in the directory the last run did not publish.</summary>
    public uint LocalChanges { get; set; }
}
