using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary><c>about</c> (CHAT-SERVICE.md §5.1): what the service is and how far it has indexed. Empty.</summary>
public sealed record AboutRequest : IChatMethod<AboutRequest, AboutReply>
{
    /// <inheritdoc />
    public static string Method => ChatMethods.About;

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out AboutRequest? value)
    {
        value = reader.Ok ? new AboutRequest() : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
    }
}
