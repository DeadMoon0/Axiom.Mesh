using Axiom.LDB.Entity;

namespace Axiom.Mesh.Sync;

/// <summary>A conflict the service recorded (SYNC-SERVICE.md §7, §10), paged by its number through the ordered index.</summary>
[LdbEntity("SyncConflicts")]
internal sealed partial class ConflictRow
{
    /// <summary>The number, 8 bytes big-endian.</summary>
    [Key]
    [Column(MaxLength = 8)]
    public byte[] Key { get; set; } = [];

    /// <summary>The number, ascending in the order recorded.</summary>
    [Index]
    public ulong Id { get; set; }

    /// <summary>The binding's id.</summary>
    [Index]
    [Column(MaxLength = 16)]
    public byte[] Binding { get; set; } = [];

    /// <summary>The path it concerns.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The <see cref="ConflictKind"/>.</summary>
    public byte Kind { get; set; }

    /// <summary>Where the copy lies, for a copy written.</summary>
    public string CopyPath { get; set; } = string.Empty;

    /// <summary>The device whose leaf lost, for a copy written.</summary>
    [Column(MaxLength = 32)]
    public byte[] OtherDevice { get; set; } = [];

    /// <summary>The Version it belongs to.</summary>
    [Column(MaxLength = 32)]
    public byte[] Version { get; set; } = [];

    /// <summary>When it was recorded, in milliseconds since the Unix epoch.</summary>
    public long AtMs { get; set; }
}
