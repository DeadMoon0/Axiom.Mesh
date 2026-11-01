using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary><c>channel</c> (CHAT-SERVICE.md §6.3): a Channel was indexed, followed, unfollowed, dropped or renamed.</summary>
public sealed record ChannelEvent : ChatEvent, IWireRecord<ChannelEvent>
{
    /// <inheritdoc />
    public override string Name => ChatEvents.Channel;

    /// <summary>What happened.</summary>
    public ChannelChange Change { get; init; }

    /// <summary>The Channel after it; for <see cref="ChannelChange.Dropped"/> its last view.</summary>
    public ChannelView Channel { get; init; } = new();

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ChannelEvent? value)
    {
        var read = new ChannelEvent
        {
            Change = (ChannelChange)reader.UInt8(),
            Channel = ChatWire.ReadRecord<ChannelView>(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        writer.UInt8((byte)Change);
        Channel.Write(ref writer);
    }
}
