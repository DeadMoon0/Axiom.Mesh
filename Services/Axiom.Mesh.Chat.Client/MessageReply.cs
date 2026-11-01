using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>The reply to <c>message</c> (CHAT-SERVICE.md §5.4).</summary>
public sealed record MessageReply : IWireRecord<MessageReply>
{
    /// <summary>The message.</summary>
    public MessageView Message { get; init; } = new();

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out MessageReply? value)
    {
        var read = new MessageReply { Message = ChatWire.ReadRecord<MessageView>(ref reader) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => Message.Write(ref writer);
}
