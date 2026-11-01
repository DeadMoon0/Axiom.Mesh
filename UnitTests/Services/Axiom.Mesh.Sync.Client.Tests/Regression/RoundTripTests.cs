using System;
using System.Linq;
using Axiom.Mesh.Sync.Client.Tests.Support;

namespace Axiom.Mesh.Sync.Client.Tests.Regression;

/// <summary>
/// Every record of <c>axiom-mesh-sync/1</c> reads back what was written, exactly to its end, and no strict prefix of it reads:
/// at every byte split a record knows where it ends.
/// </summary>
public sealed class RoundTripTests
{
    public static TheoryData<string> Types() => [.. SyncSamples.Types().Select(t => t.Name)];

    // Damage: a record whose reader stopped early or ran on would read a list of bindings as garbage from the second on, and a
    // truncated payload would read as a shorter valid record instead of failing.
    [Theory]
    [MemberData(nameof(Types))]
    public void EveryRecordReadsBackWhatWasWritten_AndNoStrictPrefixReads(string name)
    {
        Type type = SyncSamples.Types().Single(t => t.Name == name);
        byte[] written = SyncSamples.Encode(SyncSamples.Of(type));

        Assert.True(SyncSamples.TryDecode(type, written, out object? read), $"{name} did not read back.");
        Assert.Equal(written, SyncSamples.Encode(read!));
        for (int length = 0; length < written.Length; length++)
            Assert.False(SyncSamples.PrefixReads(type, written[..length]), $"{name} read from its first {length} of {written.Length} bytes.");
    }
}
