using Axiom.Mesh.Chat.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat.Tests.Rules;

/// <summary>
/// <c>send</c> (CHAT-SERVICE.md §5.6, PAYLOADS.md §3): a message goes to a Channel followed here, says something, names
/// each attachment, and replies only to a message of the same Channel that is indexed.
/// </summary>
public sealed class SendRuleTests
{
    // §5.6: a text alone, an attachment alone, or both, to a followed Channel; a reply to a message indexed there.
    [Fact]
    public void AMessageWithATextOrAnAttachmentToAFollowedChannelIsWritten_AReplyToAnIndexedMessageToo()
    {
        Assert.True(SendRule.Check(new SendRequest { PackageId = Facts.Family, Text = "hi" }, channelFollowed: true, replyToInChannel: false).IsPass);
        Assert.True(SendRule.Check(new SendRequest { PackageId = Facts.Family, Attachments = [Facts.Attachment("a.jpg", 9, 10)] }, true, false).IsPass);
        Assert.True(SendRule.Check(new SendRequest { PackageId = Facts.Family, Text = "re", ReplyTo = Facts.Hash(3) }, true, replyToInChannel: true).IsPass);
    }

    // §5.6, §7: a Channel not followed here is NotAChannel; a message saying nothing or an unnamed attachment is Invalid;
    // a reply to a message not indexed in that Channel is NotAMessage.
    [Theory]
    [InlineData(false, "hi", "a.jpg", false, 0, ChatStatus.NotAChannel)] // the Channel is not followed here
    [InlineData(true, "", "", false, 0, ChatStatus.None)] // neither text nor attachment: Invalid (no chat status)
    [InlineData(true, "hi", "-", false, 0, ChatStatus.None)] // an attachment without a name: Invalid
    [InlineData(true, "hi", "", false, 3, ChatStatus.NotAMessage)] // a reply to a message not indexed in the Channel
    public void WhatTheRuleRefuses(bool followed, string text, string attachment, bool replyIndexed, byte replyTo, ChatStatus expected)
    {
        var request = new SendRequest
        {
            PackageId = Facts.Family,
            Text = text,
            Attachments = attachment.Length == 0 ? [] : [Facts.Attachment(attachment == "-" ? string.Empty : attachment, 9, 1)],
            ReplyTo = replyTo == 0 ? default : Facts.Hash(replyTo),
        };

        Verdict verdict = SendRule.Check(request, followed, replyIndexed);

        Assert.False(verdict.IsPass);
        Assert.Equal(expected == ChatStatus.None ? Status.Invalid : (Status)(ushort)expected, verdict.Status);
    }
}
