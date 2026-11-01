using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>
/// One message as an app shows it (CHAT-SERVICE.md §4). A withdrawn or broken message carries neither its text nor its
/// attachments: the service never hands out what SPEC.md §9.4 withdrew or what PAYLOADS.md §1.2 calls the writer's fault.
/// </summary>
public sealed record MessageView : IWireRecord<MessageView>
{
    /// <summary>Its position in the Channel's total order (SPEC.md §9.3); <see cref="EntryKey.Hash"/> is the Entry's hash.</summary>
    public EntryKey Key { get; init; }

    /// <summary>Its Channel.</summary>
    public PackageId PackageId { get; init; }

    /// <summary>Who wrote it.</summary>
    public AuthorView Author { get; init; } = new();

    /// <summary>When this Host stored it, in milliseconds since the Unix epoch.</summary>
    public ulong Stored { get; init; }

    /// <summary>The text; empty when withdrawn or broken.</summary>
    public string Text { get; init; } = "";

    /// <summary>The attachments, in the writer's order; empty when withdrawn or broken.</summary>
    public IReadOnlyList<Attachment> Attachments { get; init; } = [];

    /// <summary>The message it answers; zero when none.</summary>
    public ObjectHash ReplyTo { get; init; }

    /// <summary>Whether an honoured Redaction withdrew it (SPEC.md §9.4).</summary>
    public bool Withdrawn { get; init; }

    /// <summary>The device whose Redaction withdrew it; zero when not withdrawn.</summary>
    public DeviceId WithdrawnBy { get; init; }

    /// <summary>Whether its payload does not parse or its envelope does not name what the payload names (PAYLOADS.md §1.2).</summary>
    public bool Broken { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out MessageView? value)
    {
        var read = new MessageView
        {
            Key = ChatWire.ReadKey(ref reader),
            PackageId = PackageId.Read(ref reader),
            Author = ChatWire.ReadRecord<AuthorView>(ref reader),
            Stored = reader.UInt64BigEndian(),
            Text = Wire.ReadString(ref reader, ChatLimits.TextLength),
            Attachments = ChatWire.ReadAttachments(ref reader),
            ReplyTo = ObjectHash.Read(ref reader),
            Withdrawn = reader.Boolean(),
            WithdrawnBy = DeviceId.Read(ref reader),
            Broken = reader.Boolean(),
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
        writer.UInt64BigEndian(Stored);
        Wire.WriteString(ref writer, Text, ChatLimits.TextLength);
        ChatWire.WriteAttachments(ref writer, Attachments);
        ReplyTo.Write(ref writer);
        writer.Boolean(Withdrawn);
        WithdrawnBy.Write(ref writer);
        writer.Boolean(Broken);
    }
}
