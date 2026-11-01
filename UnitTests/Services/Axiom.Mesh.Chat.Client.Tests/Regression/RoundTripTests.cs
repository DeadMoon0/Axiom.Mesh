using System;
using System.Linq;
using Axiom.Mesh.Chat.Client.Tests.Support;

namespace Axiom.Mesh.Chat.Client.Tests.Regression;

/// <summary>
/// Every record of <c>axiom-mesh-chat/1</c> reads back what was written, exactly to its end, and no strict prefix of it
/// reads: at every byte split a record knows where it ends.
/// </summary>
public sealed class RoundTripTests
{
    public static TheoryData<string> Types() => [.. ChatSamples.Types().Select(t => t.Name)];

    // Damage: a record whose reader stopped early or ran on would read a page of messages as garbage from the second on,
    // and a truncated payload would read as a shorter valid record instead of failing.
    [Theory]
    [MemberData(nameof(Types))]
    public void EveryRecordReadsBackWhatWasWritten_AndNoStrictPrefixReads(string name)
    {
        Type type = ChatSamples.Types().Single(t => t.Name == name);
        byte[] written = ChatSamples.Encode(ChatSamples.Of(type));

        Assert.True(ChatSamples.TryDecode(type, written, out object? read), $"{name} did not read back.");
        Assert.Equal(written, ChatSamples.Encode(read!));
        for (int length = 0; length < written.Length; length++)
            Assert.False(ChatSamples.PrefixReads(type, written[..length]), $"{name} read from its first {length} of {written.Length} bytes.");
    }
}
