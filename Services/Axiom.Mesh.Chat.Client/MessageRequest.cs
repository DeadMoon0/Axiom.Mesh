using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary><c>message</c> (CHAT-SERVICE.md §5.4): one message by its Entry's hash.</summary>
public sealed record MessageRequest : IChatMethod<MessageRequest, MessageReply>
{
    /// <inheritdoc />
    public static string Method => ChatMethods.Message;

    /// <summary>The message's Entry hash.</summary>
    public ObjectHash Hash { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out MessageRequest? value)
    {
        var read = new MessageRequest { Hash = ObjectHash.Read(ref reader) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => Hash.Write(ref writer);
}
