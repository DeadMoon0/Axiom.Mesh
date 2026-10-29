using System;

namespace Axiom.Mesh.Protocol.Tests.Rules;

/// <summary>The bound on outstanding wants (SPEC.md §7.5) and on queued object bytes (§7.6).</summary>
public sealed class WantBudgetTests
{
    // §7.5: "A node MUST NOT have more than 64 hashes outstanding (wanted, not yet answered or cancelled) toward one peer;
    // a Want that would exceed that on the receiving side is a violation."
    [Fact]
    public void A65thOutstandingWantIsAViolation()
    {
        Assert.True(WantBudget.Empty.TryWant(40, out WantBudget forty));
        Assert.True(forty.TryWant(24, out WantBudget full));
        Assert.Equal(64, full.Outstanding);
        Assert.Equal(0, full.Remaining);

        Assert.False(full.TryWant(1, out WantBudget unchanged));
        Assert.Equal(64, unchanged.Outstanding);
        Assert.False(WantBudget.Empty.TryWant(65, out _));

        // An answer or a cancel frees exactly what it settles.
        WantBudget settled = full.Settle(1);
        Assert.True(settled.TryWant(1, out _));
        Assert.False(settled.TryWant(2, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => WantBudget.Empty.Settle(1));
    }

    // §7.6: "A sender MUST NOT have more than 8 MiB of Object payload queued toward one peer. Past that it ... defers
    // answering further Wants until the queue drains."
    [Fact]
    public void AnObjectThatWouldPass8MiBQueuedIsDeferred_AtExactly8MiBItIsQueued()
    {
        Assert.True(ObjectQueueRule.Empty.TryQueue(Limits.MaxObjectLength, out ObjectQueueRule four));
        Assert.True(four.TryQueue(Limits.MaxObjectLength, out ObjectQueueRule eight));
        Assert.Equal(Limits.MaxQueuedObjectBytes, eight.QueuedBytes);
        Assert.True(eight.DefersWants);

        Assert.False(eight.TryQueue(1, out ObjectQueueRule unchanged));
        Assert.Equal(Limits.MaxQueuedObjectBytes, unchanged.QueuedBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => ObjectQueueRule.Empty.TryQueue(Limits.MaxObjectLength + 1, out _));

        ObjectQueueRule drained = eight.Dequeue(1);
        Assert.False(drained.DefersWants);
        Assert.True(drained.TryQueue(1, out _));
        Assert.False(drained.TryQueue(2, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => ObjectQueueRule.Empty.Dequeue(1));
    }
}
