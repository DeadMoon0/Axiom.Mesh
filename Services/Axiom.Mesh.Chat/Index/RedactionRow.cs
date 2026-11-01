using Axiom.LDB.Entity;

namespace Axiom.Mesh.Chat;

/// <summary>
/// A redaction indexed (CHAT-SERVICE.md §10): kept whether or not its target is known yet, so that a Redaction arriving
/// before its target is applied when the target comes (SPEC.md §9.4, both arrival orders).
/// </summary>
[LdbEntity("ChatRedactions")]
internal sealed partial class RedactionRow
{
    /// <summary>The redaction Entry's hash.</summary>
    [Key]
    [Column(MaxLength = 32)]
    public byte[] Hash { get; set; } = [];

    /// <summary>The Entry its envelope's <c>redacts</c> names.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Target { get; set; } = [];

    /// <summary>Its Channel.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Package { get; set; } = [];

    /// <summary>The writing device.</summary>
    [Column(MaxLength = 32)]
    public byte[] Device { get; set; } = [];

    /// <summary>Its Lamport clock.</summary>
    public ulong Clock { get; set; }

    /// <summary>The store sequence it was taken in with; 0 from a history rebuild or a <c>withdraw</c>.</summary>
    public ulong StoreSeq { get; set; }
}
