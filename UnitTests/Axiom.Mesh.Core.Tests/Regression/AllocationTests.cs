using System;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Core.Tests.Regression;

/// <summary>The allocation budget of intake (plan §3.5): an object is copied once, into a pooled buffer.</summary>
public sealed class AllocationTests
{
    private const int Budget = 16 * 1024;

    // Damage: an intake that copied a received Chunk into a fresh array, or parsed it into one, would allocate a mebibyte
    // per Chunk a peer sends; the copy comes from the pool and goes back once the file is written.
    [ReleaseFact]
    public async Task AdmittingAMebibyteChunkAllocatesOnlyThePooledCopy()
    {
        await using TestStore node = await TestStore.StartAsync();
        node.Wanted.All = true;
        var origin = Origin.FromPeer(TestKeys.BobDesktop.PublicKey.AsDeviceId());
        long least = long.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            Chunk chunk = Samples.Chunk((byte)i, Limits.ChunkLength);
            ReadOnlyMemory<byte> frame = chunk.Bytes;
            long before = GC.GetAllocatedBytesForCurrentThread();
            IntakeOutcome outcome = await node.Intake.ReceiveAsync(chunk.Hash, frame, origin, null, node.Now, default);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(AdmitOutcome.Stored, (await outcome.Stored!).Outcome);
            await node.Writer.FlushAsync(default); // the batch after it begins only once the buffer went back to the pool
            if (i >= 2)
                least = Math.Min(least, allocated); // the first rounds fill the pool
        }

        // The least of the rounds after the pool filled: the pooled copy's cost shows in every round, a rent that found
        // the pool emptied by another test's thread only in some (see Allocations).
        Assert.InRange(least, 1, Budget);
    }
}
