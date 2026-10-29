using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Axiom.Mesh.Core.Tests.Regression;

/// <summary>The node's facts: numbered in order, the newest kept in a bounded ring, and waited for without a gap.</summary>
public sealed class NodeFactsTests
{
    // Damage: a test, the CLI's watch or a log reader that reads the facts out of order, or misses one recorded between
    // reading LastSequence and waiting, waits for ever or tells a peer's story backwards.
    [Fact]
    public async Task FactsAreNumberedInOrder_TheRingKeepsTheNewest_AndAWaitSeesHeldAndLaterFacts()
    {
        var facts = new NodeFacts(new FakeTimeProvider(TestIdentity.Epoch), NullLogger.Instance, 3);
        for (int i = 0; i < 5; i++)
            facts.Record(NodeFactKind.Accepted, i + 1, null, default, string.Empty);

        Assert.Equal(5, facts.LastSequence);
        Assert.Equal([3L, 4L, 5L], facts.Recent().Select(f => f.Sequence));
        Assert.Equal(4, (await facts.WaitForAsync(f => f.Connection == 4, 0, CancellationToken.None)).Sequence);

        Task<NodeFact> later = facts.WaitForAsync(f => f.Kind == NodeFactKind.Closed, facts.LastSequence, CancellationToken.None);
        Assert.False(later.IsCompleted);
        facts.Record(NodeFactKind.Closed, 9, null, default, "done");
        Assert.Equal(6, (await later.WaitAsync(TestMesh.Deadline)).Sequence);
    }

    // Damage: a connection's facts out of order (closed before established) make every wait on them, and every log of
    // them, tell the wrong story.
    [Fact]
    public async Task OneConnectionsFactsComeAcceptedOpenedEstablishedEndedClosed()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(
            TestMeshOptions.Of("alice-laptop", "bob-desktop") with { Trust = TrustSeed.MutuallyKnown("alice-laptop", "bob-desktop") },
            CancellationToken.None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", CancellationToken.None);
        long connection = mesh["bob-desktop"].Node.Table.EstablishedWith(mesh["alice-laptop"].Device)!.Connection;

        await mesh.StopNodeAsync("alice-laptop", CancellationToken.None);
        await mesh.WaitForFactAsync("bob-desktop", f => f.Kind == NodeFactKind.Closed && f.Connection == connection, 0, "the connection closed", CancellationToken.None);

        NodeFactKind[] story = [.. mesh["bob-desktop"].Node.Facts.Recent().Where(f => f.Connection == connection).Select(f => f.Kind)];
        Assert.Equal([NodeFactKind.Accepted, NodeFactKind.SessionOpened, NodeFactKind.Established, NodeFactKind.SessionEnded, NodeFactKind.Closed], story);
    }
}
