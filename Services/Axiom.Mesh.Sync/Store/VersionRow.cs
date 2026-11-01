using Axiom.LDB.Entity;

namespace Axiom.Mesh.Sync;

/// <summary>
/// One Version Entry of an Archive (SYNC-SERVICE.md §10): the DAG the leaves and ancestors are computed on. A cache of what the
/// Host stores, rebuilt by store sequence and discarded on another store epoch.
/// </summary>
[LdbEntity("SyncVersions")]
internal sealed partial class VersionRow
{
    /// <summary>The Entry's hash.</summary>
    [Key]
    [Column(MaxLength = 32)]
    public byte[] Hash { get; set; } = [];

    /// <summary>Its Archive.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Package { get; set; } = [];

    /// <summary>Its Lamport clock.</summary>
    public ulong Clock { get; set; }

    /// <summary>The writing device.</summary>
    [Column(MaxLength = 32)]
    public byte[] Device { get; set; } = [];

    /// <summary>Its parents, 32 bytes each.</summary>
    public byte[] Parents { get; set; } = [];

    /// <summary>Its branch.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The Tree it names.</summary>
    [Column(MaxLength = 32)]
    public byte[] Tree { get; set; } = [];

    /// <summary>The store sequence it came with; 0 for one this service wrote and recorded before the stream delivered it.</summary>
    public ulong StoreSeq { get; set; }
}
