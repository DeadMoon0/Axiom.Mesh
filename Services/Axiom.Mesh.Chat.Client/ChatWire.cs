using System;
using System.Collections.Generic;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>
/// The structures the records of <c>axiom-mesh-chat/1</c> share (CHAT-SERVICE.md §4), over the types of SPEC.md §5.2:
/// an <c>EntryKey</c> as LOCAL-SPEC.md §5.6 lays it out, <c>optional&lt;T&gt;</c> as a <c>bool</c> and the value,
/// attachments as PAYLOADS.md §3 lays them out, lists of records. Readers never throw and fail the reader on a value out of
/// bounds; writers refuse one with an <see cref="ArgumentException"/>.
/// </summary>
internal static class ChatWire
{
    /// <summary>Reads an <c>EntryKey</c>: clock, device id, hash.</summary>
    public static EntryKey ReadKey(ref WireReader reader)
    {
        ulong clock = reader.UInt64BigEndian();
        DeviceId device = DeviceId.Read(ref reader);
        ObjectHash hash = ObjectHash.Read(ref reader);
        return new EntryKey(clock, device, hash);
    }

    /// <summary>Writes an <c>EntryKey</c>.</summary>
    public static void WriteKey(ref WireWriter writer, EntryKey key)
    {
        writer.UInt64BigEndian(key.Clock);
        key.Device.Write(ref writer);
        key.Hash.Write(ref writer);
    }

    /// <summary>Reads an <c>optional&lt;EntryKey&gt;</c>.</summary>
    public static EntryKey? ReadOptionalKey(ref WireReader reader) => reader.Boolean() ? ReadKey(ref reader) : null;

    /// <summary>Writes an <c>optional&lt;EntryKey&gt;</c>.</summary>
    public static void WriteOptionalKey(ref WireWriter writer, EntryKey? key)
    {
        writer.Boolean(key is not null);
        if (key is { } present)
            WriteKey(ref writer, present);
    }

    /// <summary>Reads an attachment: name <c>string(255)</c>, hash, size <c>u64</c>.</summary>
    public static Attachment ReadAttachment(ref WireReader reader)
    {
        string name = Wire.ReadString(ref reader, ChatLimits.AttachmentNameLength);
        ObjectHash hash = ObjectHash.Read(ref reader);
        ulong size = reader.UInt64BigEndian();
        return new Attachment(name, hash, size);
    }

    /// <summary>Writes an attachment.</summary>
    public static void WriteAttachment(ref WireWriter writer, Attachment attachment)
    {
        Wire.WriteString(ref writer, attachment.Name ?? string.Empty, ChatLimits.AttachmentNameLength);
        attachment.Hash.Write(ref writer);
        writer.UInt64BigEndian(attachment.Size);
    }

    /// <summary>Reads a <c>list&lt;Attachment&gt;(64)</c>.</summary>
    public static IReadOnlyList<Attachment> ReadAttachments(ref WireReader reader)
    {
        int count = Wire.ReadListCount(ref reader, ChatLimits.Attachments);
        if (count == 0)
            return [];
        var attachments = new Attachment[count];
        for (int i = 0; i < count && reader.Ok; i++)
            attachments[i] = ReadAttachment(ref reader);
        return reader.Ok ? attachments : [];
    }

    /// <summary>Writes a <c>list&lt;Attachment&gt;(64)</c>.</summary>
    public static void WriteAttachments(ref WireWriter writer, IReadOnlyList<Attachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        Wire.WriteListCount(ref writer, attachments.Count, ChatLimits.Attachments);
        for (int i = 0; i < attachments.Count; i++)
            WriteAttachment(ref writer, attachments[i]);
    }

    /// <summary>Reads an <c>optional&lt;Attachment&gt;</c>.</summary>
    public static Attachment? ReadOptionalAttachment(ref WireReader reader) => reader.Boolean() ? ReadAttachment(ref reader) : null;

    /// <summary>Writes an <c>optional&lt;Attachment&gt;</c>.</summary>
    public static void WriteOptionalAttachment(ref WireWriter writer, Attachment? attachment)
    {
        writer.Boolean(attachment is not null);
        if (attachment is { } present)
            WriteAttachment(ref writer, present);
    }

    /// <summary>Reads one record; only meaningful while <c>reader.Ok</c>.</summary>
    public static T ReadRecord<T>(ref WireReader reader)
        where T : class, IWireRecord<T> =>
        T.TryRead(ref reader, out T? value) ? value : null!;

    /// <summary>Reads a <c>list&lt;T&gt;(max)</c> of records.</summary>
    public static IReadOnlyList<T> ReadList<T>(ref WireReader reader, int maxCount)
        where T : class, IWireRecord<T>
    {
        int count = Wire.ReadListCount(ref reader, maxCount);
        if (count == 0)
            return [];
        var items = new T[count];
        for (int i = 0; i < count; i++)
        {
            if (!T.TryRead(ref reader, out T? item))
                return [];
            items[i] = item;
        }

        return items;
    }

    /// <summary>Writes a <c>list&lt;T&gt;(max)</c> of records.</summary>
    public static void WriteList<T>(ref WireWriter writer, IReadOnlyList<T> items, int maxCount)
        where T : class, IWireRecord<T>
    {
        ArgumentNullException.ThrowIfNull(items);
        Wire.WriteListCount(ref writer, items.Count, maxCount);
        for (int i = 0; i < items.Count; i++)
            items[i].Write(ref writer);
    }
}
