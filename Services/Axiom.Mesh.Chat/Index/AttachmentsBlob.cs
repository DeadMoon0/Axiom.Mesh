using System;
using System.Buffers;
using System.Collections.Generic;
using Axiom.Mesh.Payloads;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>A message's attachments in the index's blob column, as PAYLOADS.md §3 lays out a <c>list&lt;Attachment&gt;(64)</c>. Pure.</summary>
internal static class AttachmentsBlob
{
    /// <summary>The most bytes the blob takes: the count and 64 attachments of a 255-byte name, a hash and a size.</summary>
    public const int MaxLength = 2 + (Limits.MaxMessageAttachments * (2 + Limits.MaxAttachmentNameLength + Hash32.Length + 8));

    /// <summary>The blob of a list of attachments.</summary>
    /// <param name="attachments">The attachments.</param>
    /// <returns>The blob.</returns>
    public static byte[] Of(IReadOnlyList<Attachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        var output = new ArrayBufferWriter<byte>();
        var writer = new WireWriter(output);
        Wire.WriteListCount(ref writer, attachments.Count, Limits.MaxMessageAttachments);
        foreach (Attachment attachment in attachments)
        {
            Wire.WriteString(ref writer, attachment.Name, Limits.MaxAttachmentNameLength);
            attachment.Hash.Write(ref writer);
            writer.UInt64BigEndian(attachment.Size);
        }

        return output.WrittenSpan.ToArray();
    }

    /// <summary>The attachments a blob holds.</summary>
    /// <param name="blob">The blob.</param>
    /// <returns>The attachments; none for an empty or damaged blob.</returns>
    public static IReadOnlyList<Attachment> Read(byte[] blob)
    {
        if (blob is null || blob.Length == 0)
            return [];
        var reader = new WireReader(blob);
        int count = Wire.ReadListCount(ref reader, Limits.MaxMessageAttachments);
        var attachments = new Attachment[count];
        for (int i = 0; i < count && reader.Ok; i++)
        {
            string name = Wire.ReadString(ref reader, Limits.MaxAttachmentNameLength);
            ObjectHash hash = ObjectHash.Read(ref reader);
            attachments[i] = new Attachment(name, hash, reader.UInt64BigEndian());
        }

        return reader.Ok ? attachments : [];
    }
}
