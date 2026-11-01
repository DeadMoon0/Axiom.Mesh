using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Chat.Tests.Support;

namespace Axiom.Mesh.Chat.Tests.Rules;

/// <summary>
/// The chat index (CHAT-SERVICE.md §10, §11) on an in-memory database: a message is indexed once whatever replays, a
/// redaction applies in both arrival orders and only when SPEC.md §9.4 honours it, a dropped Channel leaves no row, a
/// discard leaves none but the pending pins.
/// </summary>
public sealed class ChatIndexTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // LOCAL-SPEC.md §5.6, §9.3: an Entry taken in twice (a replay after a crash) is indexed once; the meta row's store
    // sequence follows the stream, not the Entries a send or a rebuild took in.
    [Fact]
    public async Task AMessageIsIndexedOnce_AReplayChangesNothing_AndTheMetaRowFollowsTheStream()
    {
        await using IndexFixture fixture = await IndexFixture.StartAsync();

        IndexResult first = await fixture.TakeInAsync(IndexFixture.Message(1, 0, Facts.Desktop, "hello", 7));
        IndexResult again = await fixture.TakeInAsync(IndexFixture.Message(1, 0, Facts.Desktop, "hello", 9));
        await fixture.TakeInAsync(IndexFixture.Message(2, 1, Facts.Laptop, "sent here", 0), advance: false);

        Assert.Equal((IndexChange.NewMessage, IndexChange.Nothing), (first.Change, again.Change));
        Assert.Equal(9ul, (await fixture.Index.ReadMetaAsync(None)).LastSeq);
        ChannelRow channel = (await fixture.Index.ChannelAsync(Facts.Family, None))!;
        Assert.Equal((2ul, 0ul), (channel.Messages, channel.Withdrawn));
        Assert.Equal(Facts.Hash(2).Value, RowBytes.Hash(channel.LastHash));
        Assert.Equal("hello", (await fixture.Index.MessageAsync(Facts.Hash(1), None))!.Text);
    }

    // SPEC.md §9.4: a Redaction before its target is kept and applied when the target comes; after it, applied at once;
    // from a device of neither the author nor the owner, kept and never applied.
    [Fact]
    public async Task ARedactionAppliesInBothArrivalOrders_AndOneSpecDoesNotHonourIsKeptButNeverApplied()
    {
        await using IndexFixture fixture = await IndexFixture.StartAsync();

        IndexResult early = await fixture.TakeInAsync(IndexFixture.Redaction(10, 3, Facts.Laptop, Facts.Hash(11), 1));
        IndexResult target = await fixture.TakeInAsync(IndexFixture.Message(11, 2, Facts.Desktop, "first", 2));
        await fixture.TakeInAsync(IndexFixture.Message(12, 4, Facts.Desktop, "second", 3));
        IndexResult late = await fixture.TakeInAsync(IndexFixture.Redaction(13, 5, Facts.Desktop, Facts.Hash(12), 4));
        await fixture.TakeInAsync(IndexFixture.Message(14, 6, Facts.Laptop, "the owner's", 5));
        IndexResult stranger = await fixture.TakeInAsync(IndexFixture.Redaction(15, 7, Facts.Desktop, Facts.Hash(14), 6));

        Assert.Equal((IndexChange.Kept, IndexChange.NewMessage, IndexChange.Withdrew, IndexChange.Kept), (early.Change, target.Change, late.Change, stranger.Change));
        Assert.True(target.Message!.Redacted);
        Assert.Equal(Facts.Desktop, late.By);
        Assert.True((await fixture.Index.MessageAsync(Facts.Hash(12), None))!.Redacted);
        Assert.False((await fixture.Index.MessageAsync(Facts.Hash(14), None))!.Redacted);
        Assert.Equal(2ul, (await fixture.Index.ChannelAsync(Facts.Family, None))!.Withdrawn);
    }

    // CHAT-SERVICE.md §10: dropping a Channel removes every row of it; a discard removes every Channel's, keeps the pending
    // pins (they are the Host's to release) and starts the meta row over.
    [Fact]
    public async Task ADroppedChannelLeavesNoRow_ADiscardLeavesNoneButThePendingPins()
    {
        await using IndexFixture fixture = await IndexFixture.StartAsync();
        await fixture.TakeInAsync(IndexFixture.Message(1, 0, Facts.Desktop, "one", 1, attachments: [Facts.Attachment("a", 0x30, 3)]));
        await fixture.TakeInAsync(IndexFixture.Redaction(2, 1, Facts.Desktop, Facts.Hash(1), 2));
        await fixture.Index.AddPendingAsync(Facts.Hash(0x40), [Facts.Hash(0x40), Facts.Hash(0x41)], 1, None);

        ChannelRow? dropped = await fixture.Index.DropChannelAsync(Facts.Family, None);
        IndexResult afterDrop = await fixture.TakeInAsync(IndexFixture.Message(3, 2, Facts.Desktop, "late", 3));
        await fixture.Index.DiscardAsync(Facts.Id(0xe2), 0, None);

        Assert.Equal("Family", dropped!.Name);
        Assert.Equal(IndexChange.Nothing, afterDrop.Change);
        Assert.Null(await fixture.Index.MessageAsync(Facts.Hash(1), None));
        Assert.Empty(await fixture.Index.ChannelsAsync(None));
        Assert.Equal((Facts.Id(0xe2), 0ul), ((await fixture.Index.ReadMetaAsync(None)).StoreEpoch, (await fixture.Index.ReadMetaAsync(None)).LastSeq));
        Assert.Equal([Facts.Hash(0x40), Facts.Hash(0x41)], await fixture.Index.TakePendingAsync([Facts.Hash(0x40)], None));
        Assert.Empty(await fixture.Index.TakeAllPendingAsync(None));
    }

    // CHAT-SERVICE.md §5.5: the chain up from a reply reaches its root; the replies under a root are found at any depth.
    [Fact]
    public async Task TheChainOfAReplyReachesItsRoot_AndTheRepliesUnderARootAreFoundAtAnyDepth()
    {
        await using IndexFixture fixture = await IndexFixture.StartAsync();
        await fixture.TakeInAsync(IndexFixture.Message(1, 0, Facts.Laptop, "root", 1));
        await fixture.TakeInAsync(IndexFixture.Message(2, 1, Facts.Desktop, "reply", 2, replyTo: Facts.Hash(1)));
        await fixture.TakeInAsync(IndexFixture.Message(3, 2, Facts.Laptop, "deeper", 3, replyTo: Facts.Hash(2)));

        var chain = await fixture.Index.ChainAsync(Facts.Hash(3), ChatLimits.ThreadDepth, None);
        var below = await fixture.Index.DescendantsAsync(Facts.Hash(1), 100, None);

        Assert.Equal(["deeper", "reply", "root"], chain.Select(r => r.Text));
        Assert.Equal(["deeper", "reply"], below.Select(r => r.Text).Order());
    }
}
