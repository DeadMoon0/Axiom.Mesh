using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>The reply to <c>timeline</c> (CHAT-SERVICE.md §5.3): cut at the limit, 256 messages and 1 MiB, whichever comes first.</summary>
public sealed record TimelineReply : IWireRecord<TimelineReply>
{
    /// <summary>The messages, in the requested order.</summary>
    public IReadOnlyList<MessageView> Messages { get; init; } = [];

    /// <summary>Whether the page was cut; ask again after its last message.</summary>
    public bool More { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out TimelineReply? value)
    {
        var read = new TimelineReply
        {
            Messages = ChatWire.ReadList<MessageView>(ref reader, ChatLimits.TimelinePage),
            More = reader.Boolean(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        ChatWire.WriteList(ref writer, Messages, ChatLimits.TimelinePage);
        writer.Boolean(More);
    }
}
