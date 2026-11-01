using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>
/// <c>timeline</c> (CHAT-SERVICE.md §5.3): a page of one Channel's messages in the total order of SPEC.md §9.3, oldest
/// first, or newest first with <see cref="Reverse"/>, after a position or from the start.
/// </summary>
public sealed record TimelineRequest : IChatMethod<TimelineRequest, TimelineReply>
{
    /// <inheritdoc />
    public static string Method => ChatMethods.Timeline;

    /// <summary>The Channel.</summary>
    public PackageId PackageId { get; init; }

    /// <summary>The last message of the previous page; absent for the first page.</summary>
    public EntryKey? After { get; init; }

    /// <summary>Newest first.</summary>
    public bool Reverse { get; init; }

    /// <summary>At most this many messages, 1 to <see cref="ChatLimits.TimelinePage"/>.</summary>
    public ushort Limit { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out TimelineRequest? value)
    {
        var read = new TimelineRequest
        {
            PackageId = PackageId.Read(ref reader),
            After = ChatWire.ReadOptionalKey(ref reader),
            Reverse = reader.Boolean(),
            Limit = reader.UInt16BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        PackageId.Write(ref writer);
        ChatWire.WriteOptionalKey(ref writer, After);
        writer.Boolean(Reverse);
        writer.UInt16BigEndian(Limit);
    }
}
