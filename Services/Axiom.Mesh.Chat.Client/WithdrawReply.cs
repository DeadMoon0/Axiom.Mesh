using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>The reply to <c>withdraw</c> (CHAT-SERVICE.md §5.7).</summary>
public sealed record WithdrawReply : IWireRecord<WithdrawReply>
{
    /// <summary>The Redaction's position in the total order; <see cref="EntryKey.Hash"/> is its Entry's hash.</summary>
    public EntryKey Key { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out WithdrawReply? value)
    {
        var read = new WithdrawReply { Key = ChatWire.ReadKey(ref reader) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => ChatWire.WriteKey(ref writer, Key);
}
