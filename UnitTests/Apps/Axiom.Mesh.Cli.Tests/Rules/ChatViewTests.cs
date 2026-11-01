using System;
using System.Linq;
using Axiom.Mesh.Chat;
using Axiom.Mesh.Cli.Tests.Support;

namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>
/// How the command line shows chat records (plan §9.3 item 4, SPEC.md §9.4, PAYLOADS.md §1.2): a withdrawn message as
/// <c>(withdrawn)</c> and a broken one as <c>(broken)</c>, never their text, in text and JSON alike; attachments with name and
/// size; a reply naming the message it answers; JSON without the local store time, so two devices print the same.
/// </summary>
public sealed class ChatViewTests
{
    // §9.4, PAYLOADS.md §1.2: withdrawn and broken show their marker and no text even when the record carried one.
    [Fact]
    public void AWithdrawnOrBrokenMessageShowsItsMarkerNeverItsText()
    {
        MessageView withdrawn = Samples.Timeline[0] with { Withdrawn = true };
        MessageView broken = Samples.Timeline[0] with { Broken = true, Attachments = [] };

        Assert.StartsWith("(withdrawn)", ChatView.Summary(withdrawn), StringComparison.Ordinal);
        Assert.Equal("(broken)", ChatView.Summary(broken));
        Assert.DoesNotContain("holiday", ChatView.Summary(withdrawn), StringComparison.Ordinal);
        Assert.DoesNotContain("holiday", string.Join("\n", ChatView.Timeline([withdrawn, broken])), StringComparison.Ordinal);
    }

    // PAYLOADS.md §3: attachments are counted and named with their size; a reply names the message it answers; the JSON
    // carries clock, device and hash but not the local store time.
    [Fact]
    public void AttachmentsShowNameAndSize_AReplyNamesWhatItAnswers_TheJsonHoldsNoLocalTime()
    {
        string summary = ChatView.Summary(Samples.Timeline[0]);
        string reply = ChatView.Summary(Samples.Timeline[2]);
        string json = ChatView.TimelineJson(Samples.Timeline);

        Assert.Equal("holiday photos [2 attachments: photo.jpg (1.0 MiB), docs (3.0 KiB)]", summary);
        Assert.EndsWith($"(reply to {Formats.Short(Samples.Timeline[0].Key.Hash.Value)})", reply, StringComparison.Ordinal);
        Assert.DoesNotContain("stored", json, StringComparison.Ordinal);
        Assert.Contains("\"clock\": 2", json, StringComparison.Ordinal);
        Assert.Equal(3, json.Split("\"entry\"").Length - 1);
        Assert.Contains("written: entry", WriteCommand.Written(Samples.Timeline[1].Key), StringComparison.Ordinal);
        Assert.Equal(1, Samples.Timeline.Count(m => m.Withdrawn));
    }
}
