using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>Plan §3.6: the change bus never blocks the writer; a subscriber that falls behind lapses alone.</summary>
public sealed class ChangeBusTests
{
    // A full subscriber queue lapses that subscription only: the writer goes on, every other subscriber receives every
    // change set, in commit order.
    [Fact]
    public async Task ASubscriberThatFallsBehindLapsesAlone_TheOthersReceiveEveryChangeSetInOrder()
    {
        await using TestStore node = await TestStore.StartAsync();
        using ChangeSubscription slow = node.Bus.Subscribe(1);
        using ChangeSubscription steady = node.Bus.Subscribe(64);

        for (byte i = 0; i < 5; i++)
            await node.PutAsync(Samples.Chunk(i));

        Assert.True(slow.Lapsed);
        Assert.False(steady.Lapsed);
        for (byte i = 0; i < 5; i++)
        {
            Assert.True(steady.Reader.TryRead(out ChangeSet? set));
            Assert.Equal(Samples.Chunk(i).Hash, Assert.Single(set.Stored).Hash);
        }

        Assert.True(slow.Reader.TryRead(out _));
        Assert.False(slow.Reader.TryRead(out _));
        Assert.True(slow.Reader.Completion.IsCompleted);
    }
}
