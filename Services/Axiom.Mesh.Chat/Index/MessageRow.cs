using Axiom.LDB.Entity;

namespace Axiom.Mesh.Chat;

/// <summary>
/// A message indexed (CHAT-SERVICE.md §10): its place in the total order, its parsed payload and whether it is withdrawn or
/// broken. The text is a second copy of what the Entry object holds (STRUCTURAL-DEBT.md 66).
/// </summary>
/// <remarks>
/// The total order (clock, device, hash) is read through the ordered index on <see cref="Clock"/> and ties of one clock
/// are put in order in memory: LDB 0.1.0 streams an order only through a one-column numeric index (as the Host's
/// <c>EntryIndex</c> does).
/// </remarks>
[LdbEntity("ChatMessages")]
internal sealed partial class MessageRow
{
    /// <summary>The Entry's hash.</summary>
    [Key]
    [Column(MaxLength = 32)]
    public byte[] Hash { get; set; } = [];

    /// <summary>Its Channel.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Package { get; set; } = [];

    /// <summary>Its Lamport clock.</summary>
    [Index]
    public ulong Clock { get; set; }

    /// <summary>The writing device.</summary>
    [Column(MaxLength = 32)]
    public byte[] Device { get; set; } = [];

    /// <summary>When the Host stored it, in milliseconds since the Unix epoch.</summary>
    public long StoredMs { get; set; }

    /// <summary>The store sequence it was taken in with; 0 when it came from a history rebuild or a <c>send</c>.</summary>
    public ulong StoreSeq { get; set; }

    /// <summary>The text; empty when broken.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The attachments, as PAYLOADS.md §3 lays out a <c>list&lt;Attachment&gt;(64)</c>.</summary>
    public byte[] Attachments { get; set; } = [];

    /// <summary>How many attachments it has.</summary>
    public int AttachmentCount { get; set; }

    /// <summary>The message it answers; <see langword="null"/> when none.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[]? ReplyTo { get; set; }

    /// <summary>Whether an honoured Redaction withdrew it.</summary>
    public bool Redacted { get; set; }

    /// <summary>The device whose Redaction withdrew it; <see langword="null"/> when not withdrawn.</summary>
    [Column(MaxLength = 32)]
    public byte[]? RedactedBy { get; set; }

    /// <summary>Whether its payload or envelope is the writer's fault (PAYLOADS.md §1.2).</summary>
    public bool Broken { get; set; }
}
