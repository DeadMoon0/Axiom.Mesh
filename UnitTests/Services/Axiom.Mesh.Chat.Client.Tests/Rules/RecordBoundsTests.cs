using System;
using System.Linq;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Chat.Client.Tests.Rules;

/// <summary>
/// The bounds of the namespace's records (CHAT-SERVICE.md §4, §9; PAYLOADS.md §3): a text of 16 384 bytes, 64 attachments,
/// a 255-byte name and a page of 256 messages are written; one more of any is refused by the writer, and a payload that
/// carries it does not read.
/// </summary>
public sealed class RecordBoundsTests
{
    // §9: at the bound the record writes and reads back; one over, the writer refuses with ArgumentException.
    [Fact]
    public void EveryBoundIsWrittenAtItsValue_AndRefusedOneOver()
    {
        var atText = new SendRequest { Text = new string('t', ChatLimits.TextLength) };
        var overText = atText with { Text = atText.Text + "t" };
        var atAttachments = new SendRequest { Text = "x", Attachments = [.. Enumerable.Range(0, ChatLimits.Attachments).Select(i => new Attachment("a", default, (ulong)i))] };
        var overAttachments = atAttachments with { Attachments = [.. atAttachments.Attachments, new Attachment("b", default, 0)] };
        var atName = new AttachRequest { Path = "/p", Name = new string('n', ChatLimits.AttachmentNameLength) };
        var atPage = new TimelineReply { Messages = [.. Enumerable.Range(0, ChatLimits.TimelinePage).Select(_ => new MessageView())] };
        var overPage = atPage with { Messages = [.. atPage.Messages, new MessageView()] };

        Assert.True(ChatCodec.TryDecode(ChatCodec.Encode(atText), out SendRequest? text) && text.Text.Length == ChatLimits.TextLength);
        Assert.True(ChatCodec.TryDecode(ChatCodec.Encode(atAttachments), out SendRequest? attachments) && attachments.Attachments.Count == ChatLimits.Attachments);
        Assert.True(ChatCodec.TryDecode(ChatCodec.Encode(atName), out AttachRequest? _));
        Assert.True(ChatCodec.TryDecode(ChatCodec.Encode(atPage), out TimelineReply? page) && page.Messages.Count == ChatLimits.TimelinePage);
        Assert.Throws<ArgumentException>(() => ChatCodec.Encode(overText));
        Assert.Throws<ArgumentException>(() => ChatCodec.Encode(overAttachments));
        Assert.Throws<ArgumentException>(() => ChatCodec.Encode(atName with { Name = atName.Name + "n" }));
        Assert.Throws<ArgumentException>(() => ChatCodec.Encode(overPage));
    }

    // §4: a payload with bytes after its record, or a list count over its bound, is no record of the namespace.
    [Fact]
    public void APayloadWithTrailingBytesOrACountOverItsBoundDoesNotRead()
    {
        byte[] reply = ChatCodec.Encode(new AttachReply { Job = 7 });
        byte[] trailing = [.. reply, 0];
        byte[] overCount = [0x01, 0x01, 0x00];

        Assert.False(ChatCodec.TryDecode(trailing, out AttachReply? _));
        Assert.False(ChatCodec.TryDecode(overCount, out ChannelsReply? _));
    }
}
