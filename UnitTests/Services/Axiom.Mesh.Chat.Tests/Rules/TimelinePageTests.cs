using System.Linq;
using Axiom.Mesh.Chat.Tests.Support;

namespace Axiom.Mesh.Chat.Tests.Rules;

/// <summary>
/// A timeline page (CHAT-SERVICE.md §5.3): the messages after the previous page's last one, cut at the limit, at 256 and
/// at the reply's byte budget, whichever comes first, saying whether something was left.
/// </summary>
public sealed class TimelinePageTests
{
    // §5.3: a page starts after the previous page's last message and stops at the limit, saying there is more; the last
    // page says there is none.
    [Fact]
    public void APageStartsAfterThePreviousOne_StopsAtTheLimit_AndSaysWhetherMoreIsLeft()
    {
        MessageView[] all = [.. Enumerable.Range(0, 5).Select(i => Facts.View(Facts.Key((ulong)i, Facts.Laptop, (byte)(i + 1)), $"m{i}"))];

        TimelineReply first = TimelinePage.Cut(all, null, reverse: false, limit: 2, ChatLimits.PayloadBytes);
        TimelineReply second = TimelinePage.Cut(all, first.Messages[^1].Key, reverse: false, limit: 2, ChatLimits.PayloadBytes);
        TimelineReply last = TimelinePage.Cut(all, second.Messages[^1].Key, reverse: false, limit: 2, ChatLimits.PayloadBytes);
        TimelineReply backward = TimelinePage.Cut([.. all.Reverse()], all[3].Key, reverse: true, limit: 10, ChatLimits.PayloadBytes);

        Assert.Equal(["m0", "m1"], first.Messages.Select(m => m.Text));
        Assert.True(first.More);
        Assert.Equal(["m2", "m3"], second.Messages.Select(m => m.Text));
        Assert.True(second.More);
        Assert.Equal(["m4"], last.Messages.Select(m => m.Text));
        Assert.False(last.More);
        Assert.Equal(["m2", "m1", "m0"], backward.Messages.Select(m => m.Text));
        Assert.False(backward.More);
    }

    // §5.3: a page never exceeds the reply's byte budget (1 MiB, LOCAL-SPEC.md §3.2 payload) nor 256 messages, whatever the
    // limit asked; what was cut is More.
    [Fact]
    public void APageNeverExceedsTheByteBudgetOr256Messages()
    {
        string big = new('x', ChatLimits.TextLength);
        MessageView[] large = [.. Enumerable.Range(0, 100).Select(i => Facts.View(Facts.Key((ulong)i, Facts.Laptop, (byte)i), big))];
        MessageView[] many = [.. Enumerable.Range(0, 300).Select(i => Facts.View(Facts.Key((ulong)i, Facts.Desktop, (byte)i), "short"))];

        TimelineReply cut = TimelinePage.Cut(large, null, reverse: false, limit: 256, byteBudget: 200_000);
        TimelineReply capped = TimelinePage.Cut(many, null, reverse: false, limit: 1_000, ChatLimits.PayloadBytes);

        Assert.True(ChatCodec.SizeOf(cut) <= 200_000);
        Assert.True(cut.More);
        Assert.Equal(12, cut.Messages.Count);
        Assert.Equal((256, true), (capped.Messages.Count, capped.More));
    }
}
