using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Chat.Tests.Support;

namespace Axiom.Mesh.Chat.Tests.Regression;

/// <summary>
/// A timeline reply fits one <c>payload</c> (LOCAL-SPEC.md §3.2, 1 MiB) however long the messages: 256 messages of 16 KiB
/// text are about 4 MiB, so the page is cut by bytes before the count and says More.
/// </summary>
public sealed class ReplyBudgetRegressionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // Damage: a page cut only at 256 messages would encode 256 × 16 KiB, about 4 MiB, which no Reply carries; the call
    // would fail for every app showing a Channel of long messages.
    [Fact]
    public async Task TwoHundredFiftySixMessagesOfSixteenKibibytesArePagedWithinOneMebibyte()
    {
        await using IndexFixture fixture = await IndexFixture.StartAsync();
        string text = new('w', ChatLimits.TextLength);
        for (int i = 0; i < ChatLimits.TimelinePage; i++)
            await fixture.TakeInAsync(IndexFixture.Message((byte)i, (ulong)(i / 2), i % 2 == 0 ? Facts.Laptop : Facts.Desktop, text, (ulong)i + 1));

        var shown = new List<MessageView>();
        EntryKey? after = null;
        int pages = 0;
        while (true)
        {
            List<MessageRow> rows = await fixture.Index.PageAsync(Facts.Family, after, false, ChatLimits.TimelinePage + 1, None);
            TimelineReply page = TimelinePage.Cut([.. rows.Select(r => RowViews.Message(r, new AuthorView()))], after, false, ChatLimits.TimelinePage, ChatLimits.PayloadBytes);
            Assert.True(ChatCodec.SizeOf(page) <= ChatLimits.PayloadBytes);
            Assert.NotEmpty(ChatCodec.Encode(page));
            shown.AddRange(page.Messages);
            pages++;
            if (!page.More)
                break;
            after = page.Messages[^1].Key;
        }

        Assert.Equal(ChatLimits.TimelinePage, shown.Select(m => m.Key).Distinct().Count());
        Assert.True(pages >= 4);
    }
}
