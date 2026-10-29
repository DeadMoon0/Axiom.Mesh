using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>SPEC.md §11.3 (eviction) and LOCAL-SPEC.md §6.5 (<c>StoreFull</c>).</summary>
public sealed class EvictionTests
{
    // SPEC.md §11.3: a node MUST NOT evict an object it promised with Have to a peer while a Want for it from that peer
    // is outstanding; once the Object was sent, it may.
    [Fact]
    public async Task AnObjectPromisedWithHaveIsNotEvictedWhileAWantForItIsOutstanding()
    {
        await using TestStore node = await TestStore.StartAsync(StoreOptions.Default with { BudgetBytes = 10 });
        Chunk promised = Samples.Chunk(1);
        await node.PutFromPeerAsync(promised, TestKeys.BobDesktop);
        DeviceId bob = TestKeys.BobDesktop.PublicKey.AsDeviceId();
        node.Store.Promises.Protect(bob, promised.Hash);

        Assert.Equal(EvictionOutcome.Promised, await node.Store.TryEvictAsync(promised.Hash, default));
        EvictionReport pass = await new Evictor(node.Writer, node.Bus).EvictNowAsync(default);
        Assert.Equal(0, pass.Evicted);
        Assert.True(await node.ContainsAsync(promised.Hash));

        node.Store.Promises.Release(bob, promised.Hash);
        Assert.Equal(EvictionOutcome.Evicted, await node.Store.TryEvictAsync(promised.Hash, default));
    }

    // SPEC.md §11.3: when the store exceeds its budget, unpinned objects go least-recently-used first until it is under
    // budget; a pinned object never goes; after eviction the object is not claimed: the change set names it and the
    // store no longer holds it.
    [Fact]
    public async Task UnpinnedObjectsGoLeastRecentlyUsedFirstUntilUnderBudget_AndAreNeverClaimedAfterwards()
    {
        const int Size = 1000;
        await using TestStore node = await TestStore.StartAsync(StoreOptions.Default with { BudgetBytes = 2 * (Size + 2) + 100 });
        Chunk[] chunks = [.. Enumerable.Range(0, 4).Select(i => Samples.Chunk((byte)i, Size))];
        foreach (Chunk chunk in chunks)
        {
            await node.PutAsync(chunk);
            node.Time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.True(node.Store.TryOpen(chunks[0].Hash, out ObjectHandle? used)); // the oldest becomes the most recently used
        used.Dispose();
        await node.Writer.FlushAsync(default);
        await node.PinsAsync(PinCommandKind.Pin, PinReason.User, default, chunks[2].Hash);
        using ChangeSubscription changes = node.Bus.Subscribe(16);

        EvictionReport pass = await new Evictor(node.Writer, node.Bus).EvictNowAsync(default);

        Assert.True(pass.UnderBudget);
        Assert.Equal(2, pass.Evicted);
        var evicted = new List<ObjectHash>();
        while (changes.Reader.TryRead(out ChangeSet? set))
            evicted.AddRange(set.Evicted);
        Assert.Equal(new[] { chunks[1].Hash, chunks[3].Hash }, evicted);
        foreach (ObjectHash gone in evicted)
        {
            Assert.False(await node.ContainsAsync(gone));
            Assert.False(node.Store.TryOpen(gone, out _));
        }

        Assert.True(await node.ContainsAsync(chunks[0].Hash));
        Assert.True(await node.ContainsAsync(chunks[2].Hash));
        Assert.Equal(2 * (Size + 2), node.Writer.Totals.Bytes);
    }

    // LOCAL-SPEC.md §6.5: a request that would store a new object fails with StoreFull when the pinned bytes plus the new
    // object exceed the budget, because eviction cannot free pinned bytes; an object a peer sends is not a request.
    [Fact]
    public async Task ALocalWriteThatWouldRaiseThePinnedBytesAboveTheBudgetIsStoreFull()
    {
        await using TestStore node = await TestStore.StartAsync(StoreOptions.Default with { BudgetBytes = 5000 });
        Chunk kept = Samples.Chunk(1, 3000);
        await node.PutAsync(kept);
        await node.PinsAsync(PinCommandKind.Pin, PinReason.User, default, kept.Hash);
        Chunk next = Samples.Chunk(2, 2500);

        Assert.Equal(AdmitOutcome.StoreFull, (await node.PutAsync(next)).Outcome);
        Assert.False(await node.ContainsAsync(next.Hash));
        Assert.Equal(AdmitOutcome.Stored, (await node.PutFromPeerAsync(next, TestKeys.BobDesktop)).Outcome);

        await node.Store.TryEvictAsync(next.Hash, default);
        await node.PinsAsync(PinCommandKind.Unpin, PinReason.User, default, kept.Hash);
        Assert.Equal(AdmitOutcome.Stored, (await node.PutAsync(next)).Outcome);
    }
}
