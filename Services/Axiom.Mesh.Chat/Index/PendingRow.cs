using Axiom.LDB.Entity;

namespace Axiom.Mesh.Chat;

/// <summary>
/// An object an <c>attach</c> put and pinned that no <c>send</c> has released yet (CHAT-SERVICE.md §8, LOCAL-SPEC.md §9.5):
/// unpinned when a message naming its attachment is written, or at the service's next start.
/// </summary>
[LdbEntity("ChatPending")]
internal sealed partial class PendingRow
{
    /// <summary>The object's hash.</summary>
    [Key]
    [Column(MaxLength = 32)]
    public byte[] Hash { get; set; } = [];

    /// <summary>The attachment it was put for: the ChunkList or root Tree.</summary>
    [Index]
    [Column(MaxLength = 32)]
    public byte[] Root { get; set; } = [];

    /// <summary>When the job that put it started, in milliseconds since the Unix epoch.</summary>
    public long StartedMs { get; set; }
}
