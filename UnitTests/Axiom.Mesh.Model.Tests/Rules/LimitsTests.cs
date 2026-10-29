namespace Axiom.Mesh.Tests.Rules;

/// <summary>The bounds of SPEC.md §5.1, §8.1 and §8.2 and LOCAL-SPEC.md §12, with the values the specifications state.</summary>
public sealed class LimitsTests
{
    // SPEC.md §5.1: "length MUST be at least 2 and at most 4 198 400 (4 MiB + 4 KiB)"; LOCAL-SPEC.md §12 repeats it.
    [Fact]
    public void AFrameIsAtLeastItsMessageIdAndAtMost4MiBPlus4KiB()
    {
        Assert.Equal(2, Limits.MinFrameLength);
        Assert.Equal((4 * 1024 * 1024) + (4 * 1024), Limits.MaxFrameLength);
    }

    // SPEC.md §8.1: "An object MUST be at most 4 MiB"; §8.2: chunks of "exactly 1 MiB except the last".
    [Fact]
    public void AnObjectIsAtMost4MiB_AndAChunkOfAFileHolds1MiB()
    {
        Assert.Equal(4 * 1024 * 1024, Limits.MaxObjectLength);
        Assert.Equal(1024 * 1024, Limits.ChunkLength);
    }

    // SPEC.md §5.1 and §7.6: a frame carries one Object of the largest size together with its header and hash.
    [Fact]
    public void TheLargestObjectFitsInOneFrame()
    {
        const int MessageId = 2;
        const int Hash = 32;
        const int BlobLength = 4;
        Assert.True(MessageId + Hash + BlobLength + Limits.MaxObjectLength <= Limits.MaxFrameLength);
    }
}
