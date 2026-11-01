using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Axiom.Mesh.Chat;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Cli;

/// <summary>
/// The chat namespace's records as the command line shows them (CHAT-SERVICE.md §4): Channels, timelines, threads, jobs and
/// the lines <c>chat watch</c> prints, as text and as JSON. A withdrawn message shows <c>(withdrawn)</c>, a broken one
/// <c>(broken)</c>, never a text; attachments as <c>[2 attachments: photo.jpg (1.0 MiB), docs (3.0 KiB)]</c>. The JSON of a
/// timeline holds only what every device agrees on (no local store time), so two devices print the same. Pure.
/// </summary>
internal static class ChatView
{
    /// <summary>The Channels as a table.</summary>
    /// <param name="channels">The Channels.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Channels(IReadOnlyList<ChannelView> channels) => TextTable.Lines(
        ["CHANNEL", "NAME", "OWNER", "MESSAGES", "WITHDRAWN", "FOLLOWED", "INDEXED"],
        channels.Select(c => (IReadOnlyList<string>)[
            Formats.Short(c.PackageId.Value),
            c.Name,
            c.OwnerName.Length > 0 ? c.OwnerName : Formats.Short(c.OwnerId.Value),
            c.Messages.ToString(CultureInfo.InvariantCulture),
            c.Withdrawn.ToString(CultureInfo.InvariantCulture),
            Formats.YesNo(c.Followed),
            Formats.YesNo(c.Indexed)]),
        "no channels");

    /// <summary>The Channels as JSON.</summary>
    /// <param name="channels">The Channels.</param>
    /// <returns>The document.</returns>
    public static string ChannelsJson(IReadOnlyList<ChannelView> channels) => JsonOut.Of(w =>
    {
        w.WriteStartArray();
        foreach (ChannelView c in channels)
        {
            w.WriteStartObject();
            JsonOut.Id(w, "packageId", c.PackageId.Value);
            w.WriteString("name", c.Name);
            JsonOut.Id(w, "ownerId", c.OwnerId.Value);
            w.WriteString("ownerName", c.OwnerName);
            w.WriteBoolean("followed", c.Followed);
            w.WriteNumber("messages", c.Messages);
            w.WriteNumber("withdrawn", c.Withdrawn);
            if (c.Last is { } last)
                w.WriteString("last", last.Hash.Value.ToHex());
            else
                w.WriteNull("last");
            w.WriteBoolean("indexed", c.Indexed);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    });

    /// <summary>A timeline as a table.</summary>
    /// <param name="messages">The messages, in the order shown.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Timeline(IReadOnlyList<MessageView> messages) => TextTable.Lines(
        ["ENTRY", "CLOCK", "AUTHOR", "STORED", "MESSAGE"],
        messages.Select(m => (IReadOnlyList<string>)[
            Formats.Short(m.Key.Hash.Value),
            m.Key.Clock.ToString(CultureInfo.InvariantCulture),
            Author(m.Author),
            Formats.Time(m.Stored),
            Summary(m)]),
        "no messages");

    /// <summary>A timeline as JSON: what every device holding the same messages agrees on.</summary>
    /// <param name="messages">The messages.</param>
    /// <returns>The document.</returns>
    public static string TimelineJson(IReadOnlyList<MessageView> messages) => JsonOut.Of(w =>
    {
        w.WriteStartArray();
        foreach (MessageView m in messages)
            Write(w, m);
        w.WriteEndArray();
    });

    /// <summary>A thread: its root, then its replies indented.</summary>
    /// <param name="thread">The thread.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Thread(ThreadReply thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        var lines = new List<string> { Line(thread.Root) };
        lines.AddRange(thread.Replies.Select(r => "  " + Line(r)));
        if (thread.More)
            lines.Add("  (more replies; ask with a higher limit)");
        return lines;
    }

    /// <summary>A thread as JSON.</summary>
    /// <param name="thread">The thread.</param>
    /// <returns>The document.</returns>
    public static string ThreadJson(ThreadReply thread) => JsonOut.Of(w =>
    {
        w.WriteStartObject();
        w.WritePropertyName("root");
        Write(w, thread.Root);
        w.WriteStartArray("replies");
        foreach (MessageView reply in thread.Replies)
            Write(w, reply);
        w.WriteEndArray();
        w.WriteBoolean("more", thread.More);
        w.WriteEndObject();
    });

    /// <summary>One message on one line: entry, author and what it says.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The line.</returns>
    public static string Line(MessageView message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return $"{Formats.Short(message.Key.Hash.Value)}  {Author(message.Author)}: {Summary(message)}";
    }

    /// <summary>What a message says, on one line: its text, or <c>(withdrawn)</c> or <c>(broken)</c>, its attachments and the message it answers.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The summary.</returns>
    public static string Summary(MessageView message)
    {
        ArgumentNullException.ThrowIfNull(message);
        string said = message.Withdrawn ? "(withdrawn)" : message.Broken ? "(broken)" : message.Text.ReplaceLineEndings(" ");
        string attached = message.Attachments.Count == 0
            ? string.Empty
            : $" [{message.Attachments.Count} attachment{(message.Attachments.Count == 1 ? string.Empty : "s")}: {string.Join(", ", message.Attachments.Select(a => $"{a.Name} ({Formats.Bytes(a.Size)})"))}]";
        string answers = message.ReplyTo.IsZero ? string.Empty : $" (reply to {Formats.Short(message.ReplyTo.Value)})";
        return said + attached + answers;
    }

    /// <summary>The line <c>chat watch</c> prints for an event; <see langword="null"/> for an event it does not show.</summary>
    /// <param name="chat">The event.</param>
    /// <returns>The line, or <see langword="null"/>.</returns>
    public static string? WatchLine(ChatEvent chat) => chat switch
    {
        MessageEvent m => $"{Formats.Short(m.Key.Hash.Value)}  {Author(m.Author)}: {m.Preview.ReplaceLineEndings(" ")}{(m.Attachments == 0 ? string.Empty : $" [{m.Attachments} attachment{(m.Attachments == 1 ? string.Empty : "s")}]")}",
        WithdrawnEvent w => $"{Formats.Short(w.Key.Hash.Value)}  withdrawn by {Formats.Short(w.By.Value)}",
        ChannelEvent c => $"channel {c.Channel.Name} ({Formats.Short(c.Channel.PackageId.Value)}): {Formats.Word(c.Change)}",
        _ => null,
    };

    /// <summary>The line a done job prints.</summary>
    /// <param name="job">The job.</param>
    /// <returns>The line.</returns>
    public static string JobDone(JobView job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Kind == JobKind.Get
            ? $"written: {job.Path} ({Formats.Bytes(job.Done)})"
            : $"attached: {job.Attachment?.Name} ({Formats.Bytes(job.Attachment?.Size ?? 0)})";
    }

    private static string Author(AuthorView author) =>
        author.UserName.Length == 0 ? Formats.Short(author.DeviceId.Value)
        : author.DeviceName.Length == 0 ? author.UserName
        : $"{author.UserName}@{author.DeviceName}";

    private static void Write(Utf8JsonWriter w, MessageView m)
    {
        w.WriteStartObject();
        JsonOut.Id(w, "entry", m.Key.Hash.Value);
        w.WriteNumber("clock", m.Key.Clock);
        JsonOut.Id(w, "deviceId", m.Key.Device.Value);
        JsonOut.Id(w, "packageId", m.PackageId.Value);
        JsonOut.Id(w, "userId", m.Author.UserId.Value);
        w.WriteString("userName", m.Author.UserName);
        w.WriteString("deviceName", m.Author.DeviceName);
        w.WriteString("text", m.Text);
        w.WriteStartArray("attachments");
        foreach (Attachment a in m.Attachments)
        {
            w.WriteStartObject();
            w.WriteString("name", a.Name);
            JsonOut.Id(w, "hash", a.Hash.Value);
            w.WriteNumber("size", a.Size);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        if (m.ReplyTo.IsZero)
            w.WriteNull("replyTo");
        else
            JsonOut.Id(w, "replyTo", m.ReplyTo.Value);
        w.WriteBoolean("withdrawn", m.Withdrawn);
        if (m.WithdrawnBy.Value.IsZero)
            w.WriteNull("withdrawnBy");
        else
            JsonOut.Id(w, "withdrawnBy", m.WithdrawnBy.Value);
        w.WriteBoolean("broken", m.Broken);
        w.WriteEndObject();
    }
}
