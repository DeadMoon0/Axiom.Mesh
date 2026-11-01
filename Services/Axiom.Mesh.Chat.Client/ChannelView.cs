using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>A Channel as the chat service indexed it (CHAT-SERVICE.md §4).</summary>
public sealed record ChannelView : IWireRecord<ChannelView>
{
    /// <summary>The Channel's Package id.</summary>
    public PackageId PackageId { get; init; }

    /// <summary>Its name, from its current Manifest.</summary>
    public string Name { get; init; } = "";

    /// <summary>Its owner.</summary>
    public UserId OwnerId { get; init; }

    /// <summary>The owner's display name; empty when this Host does not know it.</summary>
    public string OwnerName { get; init; } = "";

    /// <summary>Whether this Host follows it; an unfollowed Channel keeps its messages until it is dropped.</summary>
    public bool Followed { get; init; }

    /// <summary>The messages indexed, withdrawn ones included.</summary>
    public ulong Messages { get; init; }

    /// <summary>How many of them are withdrawn.</summary>
    public ulong Withdrawn { get; init; }

    /// <summary>The position of the last message in the total order; absent when it has none.</summary>
    public EntryKey? Last { get; init; }

    /// <summary>Whether the whole history this Host holds is indexed; until then a timeline may lack older messages.</summary>
    public bool Indexed { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ChannelView? value)
    {
        var read = new ChannelView
        {
            PackageId = PackageId.Read(ref reader),
            Name = Wire.ReadString(ref reader, ChatLimits.ChannelNameLength),
            OwnerId = UserId.Read(ref reader),
            OwnerName = Wire.ReadString(ref reader, ChatLimits.UserNameLength),
            Followed = reader.Boolean(),
            Messages = reader.UInt64BigEndian(),
            Withdrawn = reader.UInt64BigEndian(),
            Last = ChatWire.ReadOptionalKey(ref reader),
            Indexed = reader.Boolean(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        PackageId.Write(ref writer);
        Wire.WriteString(ref writer, Name, ChatLimits.ChannelNameLength);
        OwnerId.Write(ref writer);
        Wire.WriteString(ref writer, OwnerName, ChatLimits.UserNameLength);
        writer.Boolean(Followed);
        writer.UInt64BigEndian(Messages);
        writer.UInt64BigEndian(Withdrawn);
        ChatWire.WriteOptionalKey(ref writer, Last);
        writer.Boolean(Indexed);
    }
}
