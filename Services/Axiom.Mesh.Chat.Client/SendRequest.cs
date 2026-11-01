using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Payloads;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>
/// <c>send</c> (CHAT-SERVICE.md §5.6): writes a Message (PAYLOADS.md §3) to a Channel. Attachments come from a done
/// <c>attach</c> job (<see cref="JobView.Attachment"/>); the service writes the Entry, then releases its pins on them
/// (LOCAL-SPEC.md §9.5).
/// </summary>
public sealed record SendRequest : IChatMethod<SendRequest, SendReply>
{
    /// <inheritdoc />
    public static string Method => ChatMethods.Send;

    /// <summary>The Channel.</summary>
    public PackageId PackageId { get; init; }

    /// <summary>The text, at most <see cref="ChatLimits.TextLength"/> bytes.</summary>
    public string Text { get; init; } = "";

    /// <summary>The attachments, at most <see cref="ChatLimits.Attachments"/>.</summary>
    public IReadOnlyList<Attachment> Attachments { get; init; } = [];

    /// <summary>The message this one answers, in the same Channel; zero for none.</summary>
    public ObjectHash ReplyTo { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out SendRequest? value)
    {
        var read = new SendRequest
        {
            PackageId = PackageId.Read(ref reader),
            Text = Wire.ReadString(ref reader, ChatLimits.TextLength),
            Attachments = ChatWire.ReadAttachments(ref reader),
            ReplyTo = ObjectHash.Read(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        PackageId.Write(ref writer);
        Wire.WriteString(ref writer, Text, ChatLimits.TextLength);
        ChatWire.WriteAttachments(ref writer, Attachments);
        ReplyTo.Write(ref writer);
    }
}
