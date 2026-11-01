using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary><c>channels</c> (CHAT-SERVICE.md §5.2): every Channel the service knows, by name. Empty.</summary>
public sealed record ChannelsRequest : IChatMethod<ChannelsRequest, ChannelsReply>
{
    /// <inheritdoc />
    public static string Method => ChatMethods.Channels;

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ChannelsRequest? value)
    {
        value = reader.Ok ? new ChannelsRequest() : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
    }
}
