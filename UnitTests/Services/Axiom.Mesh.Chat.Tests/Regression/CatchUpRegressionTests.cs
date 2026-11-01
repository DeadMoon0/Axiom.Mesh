using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Chat.Tests.Support;

namespace Axiom.Mesh.Chat.Tests.Regression;

/// <summary>
/// The runtime commits its cursor after the handler's transaction (S0), so a crash between the two hands the last Entries
/// over again, and a rebuild from the history meets Entries the stream delivers later: neither may count a message twice.
/// </summary>
public sealed class CatchUpRegressionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // Damage: an index that counted by arrival instead of deduplicating by hash would show a message twice after a crash
    // between its commit and the cursor's, or after a rebuild that a replay overlapped; the counts would drift for ever.
    [Fact]
    public async Task AReplayBetweenThePageAndTheCursorCommitCountsNothingTwice()
    {
        await using IndexFixture fixture = await IndexFixture.StartAsync();
        for (byte i = 1; i <= 5; i++)
            await fixture.TakeInAsync(IndexFixture.Message(i, i, Facts.Desktop, $"m{i}", i), advance: false);
        await fixture.TakeInAsync(IndexFixture.Redaction(9, 9, Facts.Desktop, Facts.Hash(3), 6), advance: false);

        for (byte i = 1; i <= 5; i++)
            await fixture.TakeInAsync(IndexFixture.Message(i, i, Facts.Desktop, $"m{i}", (ulong)i + 10));
        await fixture.TakeInAsync(IndexFixture.Redaction(9, 9, Facts.Desktop, Facts.Hash(3), 16));

        ChannelRow channel = (await fixture.Index.ChannelAsync(Facts.Family, None))!;
        Assert.Equal((5ul, 1ul), (channel.Messages, channel.Withdrawn));
        Assert.Equal(5, (await fixture.KeysAsync(reverse: false, 2)).Count);
        Assert.Equal(16ul, (await fixture.Index.ReadMetaAsync(None)).LastSeq);
    }
}
