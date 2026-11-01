using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>The reply to <c>thread</c> (CHAT-SERVICE.md §5.5).</summary>
public sealed record ThreadReply : IWireRecord<ThreadReply>
{
    /// <summary>The thread's root; a withdrawn root keeps its replies.</summary>
    public MessageView Root { get; init; } = new();

    /// <summary>The replies under the root, at any depth, in the total order.</summary>
    public IReadOnlyList<MessageView> Replies { get; init; } = [];

    /// <summary>Whether the replies were cut at the limit or at 1 MiB.</summary>
    public bool More { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ThreadReply? value)
    {
        var read = new ThreadReply
        {
            Root = ChatWire.ReadRecord<MessageView>(ref reader),
            Replies = ChatWire.ReadList<MessageView>(ref reader, ChatLimits.TimelinePage),
            More = reader.Boolean(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Root.Write(ref writer);
        ChatWire.WriteList(ref writer, Replies, ChatLimits.TimelinePage);
        writer.Boolean(More);
    }
}
