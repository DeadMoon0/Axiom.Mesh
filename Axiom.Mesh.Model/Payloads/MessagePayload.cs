using System.Collections.Generic;
using System.Linq;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Payloads;

/// <summary>A message in a Channel (PAYLOADS.md §3, kind 2).</summary>
public sealed class MessagePayload : EntryPayload
{
    /// <summary>The text; at most 16 384 bytes.</summary>
    public required string Text { get; init; }

    /// <summary>The attachments, in the writer's order; at most 64.</summary>
    public required IReadOnlyList<Attachment> Attachments { get; init; }

    /// <summary>The Entry this one answers; zero when none.</summary>
    public required ObjectHash ReplyTo { get; init; }

    /// <inheritdoc />
    public override EntryKind Kind => EntryKind.Message;

    /// <inheritdoc />
    public override IReadOnlyList<ObjectHash> References => [.. Attachments.Select(a => a.Hash)];

    internal static MessagePayload Read(ref WireReader reader)
    {
        string text = Wire.ReadString(ref reader, Limits.MaxMessageTextLength);
        int count = Wire.ReadListCount(ref reader, Limits.MaxMessageAttachments);
        var attachments = new Attachment[count];
        for (int i = 0; i < count && reader.Ok; i++)
        {
            string name = Wire.ReadString(ref reader, Limits.MaxAttachmentNameLength);
            ObjectHash hash = ObjectHash.Read(ref reader);
            attachments[i] = new Attachment(name, hash, reader.UInt64BigEndian());
        }

        return new MessagePayload { Text = text, Attachments = attachments, ReplyTo = ObjectHash.Read(ref reader) };
    }

    internal void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Text, Limits.MaxMessageTextLength);
        Wire.WriteListCount(ref writer, Attachments.Count, Limits.MaxMessageAttachments);
        foreach (Attachment attachment in Attachments)
        {
            Wire.WriteString(ref writer, attachment.Name, Limits.MaxAttachmentNameLength);
            attachment.Hash.Write(ref writer);
            writer.UInt64BigEndian(attachment.Size);
        }

        ReplyTo.Write(ref writer);
    }
}
