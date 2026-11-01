using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>
/// <c>withdraw</c> (CHAT-SERVICE.md §5.7): writes a Redaction (PAYLOADS.md §4) of a message. Only the message's own device
/// or a device of the Channel's owner may (SPEC.md §9.4); the service checks first, because the Host would store a
/// Redaction it does not honour without a word.
/// </summary>
public sealed record WithdrawRequest : IChatMethod<WithdrawRequest, WithdrawReply>
{
    /// <inheritdoc />
    public static string Method => ChatMethods.Withdraw;

    /// <summary>The message's Entry hash.</summary>
    public ObjectHash Message { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out WithdrawRequest? value)
    {
        var read = new WithdrawRequest { Message = ObjectHash.Read(ref reader) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => Message.Write(ref writer);
}
