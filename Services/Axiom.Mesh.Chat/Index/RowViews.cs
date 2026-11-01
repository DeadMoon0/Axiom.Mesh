using System;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Chat;

/// <summary>
/// The index's rows as the namespace's records (CHAT-SERVICE.md §4): a withdrawn or broken message keeps its place and
/// loses its text and attachments (<see cref="Timeline.Visible"/>). Pure.
/// </summary>
internal static class RowViews
{
    /// <summary>A message as an app sees it.</summary>
    /// <param name="row">The message's row.</param>
    /// <param name="author">Who wrote it.</param>
    /// <returns>The view.</returns>
    public static MessageView Message(MessageRow row, AuthorView author)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(author);
        bool visible = Timeline.Visible(row.Redacted, row.Broken);
        return new MessageView
        {
            Key = ChatIndex.KeyOf(row),
            PackageId = new PackageId(RowBytes.Hash(row.Package)),
            Author = author,
            Stored = (ulong)Math.Max(row.StoredMs, 0),
            Text = visible ? row.Text : string.Empty,
            Attachments = visible ? AttachmentsBlob.Read(row.Attachments) : [],
            ReplyTo = new ObjectHash(RowBytes.Hash(row.ReplyTo)),
            Withdrawn = row.Redacted,
            WithdrawnBy = new DeviceId(RowBytes.Hash(row.RedactedBy)),
            Broken = row.Broken,
        };
    }

    /// <summary>The <c>message</c> event of a message just indexed or written.</summary>
    /// <param name="row">The message's row; neither withdrawn nor broken.</param>
    /// <param name="author">Who wrote it.</param>
    /// <returns>The event.</returns>
    public static MessageEvent Event(MessageRow row, AuthorView author)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new MessageEvent
        {
            Key = ChatIndex.KeyOf(row),
            PackageId = new PackageId(RowBytes.Hash(row.Package)),
            Author = author,
            Preview = Timeline.Preview(row.Text, ChatLimits.PreviewLength),
            Attachments = (ushort)row.AttachmentCount,
            ReplyTo = new ObjectHash(RowBytes.Hash(row.ReplyTo)),
        };
    }

    /// <summary>A Channel as an app sees it.</summary>
    /// <param name="row">The Channel's row.</param>
    /// <param name="ownerName">The owner's display name; empty when unknown.</param>
    /// <returns>The view.</returns>
    public static ChannelView Channel(ChannelRow row, string ownerName)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new ChannelView
        {
            PackageId = new PackageId(RowBytes.Hash(row.PackageId)),
            Name = row.Name,
            OwnerId = new UserId(RowBytes.Hash(row.OwnerId)),
            OwnerName = ownerName ?? string.Empty,
            Followed = row.Followed,
            Messages = row.Messages,
            Withdrawn = row.Withdrawn,
            Last = row.LastHash is null ? null : new EntryKey(row.LastClock, new DeviceId(RowBytes.Hash(row.LastDevice)), new ObjectHash(RowBytes.Hash(row.LastHash))),
            Indexed = row.Indexed,
        };
    }

    /// <summary>An attachment as a message names it.</summary>
    /// <param name="name">The name shown.</param>
    /// <param name="hash">The ChunkList or Tree.</param>
    /// <param name="size">Its size, for display.</param>
    /// <returns>The attachment.</returns>
    public static Attachment Attachment(string name, ObjectHash hash, ulong size) => new(name, hash, size);
}
