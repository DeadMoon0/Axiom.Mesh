using System;
using System.Linq;
using Axiom.Mesh.Tests.Shared;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Regression;

/// <summary>The allocation budgets of the hot paths (plan §3.5): ids inline, hashing on spans, parsing in place.</summary>
public sealed class AllocationTests
{
    // Damage: a hash or id that boxed, copied to an array or built a string on comparison would allocate on every object
    // a node receives, sends or indexes.
    [ReleaseFact]
    public void HashingAMebibyteAndComparingIdsAllocatesNothing()
    {
        byte[] chunk = new byte[Limits.ChunkLength];
        ObjectHash other = RawObjects.Hash(7);
        Measure(() => _ = ObjectHash.Of(chunk) < other);
        Measure(() => _ = Digests.Of(SigningDomain.Entry, chunk));

        long allocated = Measure(() =>
        {
            ObjectHash hash = ObjectHash.Of(chunk);
            _ = hash == other && hash.CompareTo(other) < 0 && HashedObject.TryVerify(chunk, hash, out _);
            _ = Digests.Of(SigningDomain.Entry, chunk);
        });

        Assert.Equal(0, allocated);
    }

    // Damage: a ChunkList that copied its 32 768 hashes into a list would allocate a megabyte per file listed; the
    // parsed object reads them in place.
    [ReleaseFact]
    public void ParsingAChunkListAllocatesOnlyTheParsedObject()
    {
        ObjectHash[] hashes = [.. Enumerable.Range(0, Limits.MaxChunkListChunks).Select(i => RawObjects.Hash((byte)i))];
        ReadOnlyMemory<byte> bytes = ObjectBuilders.ChunkList(1, hashes).Bytes;
        HashedObject hashed = HashedObject.Of(bytes);

        long allocated = Measure(() => Objects.TryParse(hashed, out _, out _));

        Assert.InRange(allocated, 1, 128);
    }

    // Damage: verifying an Entry that copied its bytes, re-encoded its body or created a key per call would make intake
    // allocate in proportion to everything it checks; a verification allocates the verified state and nothing else that
    // grows with the Entry.
    [ReleaseFact]
    public void VerifyingAnEntryAllocatesNothingThatGrowsWithTheEntry()
    {
        string text = new('x', Limits.MaxMessageTextLength);
        VerifiedEntry entry = Samples.Message(TestKeys.AliceLaptop, Samples.Channel().Content.Package, 0, EntryParents.None, text);
        UnverifiedEntry parsed = Samples.Parse<UnverifiedEntry>(entry.Bytes);
        PublicKey key = TestKeys.AliceLaptop.PublicKey;

        long allocated = Measure(() => VerifiedEntry.TryVerify(parsed, key, out _));

        // The signature check converts P1363 to DER for the platform library: about 1 KiB with CNG on Windows, about 1.4 KiB
        // with OpenSSL on Linux; the text alone is 16 KiB, so a copy would show.
        Assert.InRange(allocated, 1, 2048);
    }

    // The least of several runs after a warm-up: a budget that the code keeps holds in every run, while a loaded
    // machine's noise (a pool rent another thread emptied, tier-1 code arriving between runs) holds in some only.
    // Damage: a file helper that built a Chunk object per part (a buffer grown to 2 MiB, a copy, a parse) would allocate
    // megabytes per mebibyte stored; sealing a reused buffer in place allocates nothing, whatever the file's size.
    [ReleaseFact]
    public void SealingAChunkInAReusedBufferAllocatesNothing()
    {
        byte[] buffer = new byte[Chunker.MaxChunkObjectLength];

        long allocated = Measure(() => Chunker.Seal(buffer));

        Assert.Equal(0, allocated);
    }

    private static long Measure(Action action) => Allocations.Least(action);
}
