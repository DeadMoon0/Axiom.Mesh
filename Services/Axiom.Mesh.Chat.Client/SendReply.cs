using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>The reply to <c>send</c> (CHAT-SERVICE.md §5.6).</summary>
public sealed record SendReply : IWireRecord<SendReply>
{
    /// <summary>The written message's position in the total order; <see cref="EntryKey.Hash"/> is its Entry's hash.</summary>
    public EntryKey Key { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out SendReply? value)
    {
        var read = new SendReply { Key = ChatWire.ReadKey(ref reader) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => ChatWire.WriteKey(ref writer, Key);
}
