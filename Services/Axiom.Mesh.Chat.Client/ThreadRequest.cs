using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>
/// <c>thread</c> (CHAT-SERVICE.md §5.5): the thread a message belongs to, its root (followed up <c>replyTo</c> as far as
/// indexed, at most <see cref="ChatLimits.ThreadDepth"/> steps) and every reply under the root in the total order.
/// </summary>
public sealed record ThreadRequest : IChatMethod<ThreadRequest, ThreadReply>
{
    /// <inheritdoc />
    public static string Method => ChatMethods.Thread;

    /// <summary>Any message of the thread.</summary>
    public ObjectHash Message { get; init; }

    /// <summary>At most this many replies, 1 to <see cref="ChatLimits.TimelinePage"/>.</summary>
    public ushort Limit { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ThreadRequest? value)
    {
        var read = new ThreadRequest { Message = ObjectHash.Read(ref reader), Limit = reader.UInt16BigEndian() };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Message.Write(ref writer);
        writer.UInt16BigEndian(Limit);
    }
}
