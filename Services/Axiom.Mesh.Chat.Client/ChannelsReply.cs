using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>The reply to <c>channels</c> (CHAT-SERVICE.md §5.2).</summary>
public sealed record ChannelsReply : IWireRecord<ChannelsReply>
{
    /// <summary>The Channels, by name then Package id, at most <see cref="ChatLimits.Channels"/>.</summary>
    public IReadOnlyList<ChannelView> Channels { get; init; } = [];

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ChannelsReply? value)
    {
        var read = new ChannelsReply { Channels = ChatWire.ReadList<ChannelView>(ref reader, ChatLimits.Channels) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => ChatWire.WriteList(ref writer, Channels, ChatLimits.Channels);
}
