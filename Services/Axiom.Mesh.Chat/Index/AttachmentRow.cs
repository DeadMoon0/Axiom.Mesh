using Axiom.LDB.Entity;

namespace Axiom.Mesh.Chat;

/// <summary>
/// One attachment of one message (CHAT-SERVICE.md §10), keyed by the attachment's hash followed by the message's
/// (a concatenated key, as STRUCTURAL-DEBT.md 15 records for LDB 0.1.0), so the messages that name an object are found.
/// </summary>
[LdbEntity("ChatAttachments")]
internal sealed partial class AttachmentRow
{
    /// <summary>The attachment's hash, then the message's: 64 bytes.</summary>
    [Key]
    [Column(MaxLength = 64)]
    public byte[] Key { get; set; } = [];

    /// <summary>The attachment's hash.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Attachment { get; set; } = [];

    /// <summary>The message's Channel.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Package { get; set; } = [];

    /// <summary>The name shown.</summary>
    [Column(MaxLength = 255)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The size the message states, for display.</summary>
    public ulong Size { get; set; }
}
