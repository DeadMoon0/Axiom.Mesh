using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>
/// <c>message</c> (CHAT-SERVICE.md §6.1): a message the service indexed while connected, or wrote for <c>send</c>; never
/// one that is withdrawn or broken, and not those it took in while catching up.
/// </summary>
public sealed record MessageEvent : ChatEvent, IWireRecord<MessageEvent>
{
    /// <inheritdoc />
    public override string Name => ChatEvents.Message;

    /// <summary>The message's position in the total order.</summary>
    public EntryKey Key { get; init; }

    /// <summary>Its Channel.</summary>
    public PackageId PackageId { get; init; }

    /// <summary>Who wrote it.</summary>
    public AuthorView Author { get; init; } = new();

    /// <summary>The start of its text, at most <see cref="ChatLimits.PreviewLength"/> bytes, cut between characters.</summary>
    public string Preview { get; init; } = "";

    /// <summary>How many attachments it has.</summary>
    public ushort Attachments { get; init; }

    /// <summary>The message it answers; zero when none.</summary>
    public ObjectHash ReplyTo { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out MessageEvent? value)
    {
        var read = new MessageEvent
        {
            Key = ChatWire.ReadKey(ref reader),
            PackageId = PackageId.Read(ref reader),
            Author = ChatWire.ReadRecord<AuthorView>(ref reader),
            Preview = Wire.ReadString(ref reader, ChatLimits.PreviewLength),
            Attachments = reader.UInt16BigEndian(),
            ReplyTo = ObjectHash.Read(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        ChatWire.WriteKey(ref writer, Key);
        PackageId.Write(ref writer);
        Author.Write(ref writer);
        Wire.WriteString(ref writer, Preview, ChatLimits.PreviewLength);
        writer.UInt16BigEndian(Attachments);
        ReplyTo.Write(ref writer);
    }
}
