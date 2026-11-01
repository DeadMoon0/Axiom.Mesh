using Axiom.Mesh.Chat.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §9.3 as the index applies it on connecting (CHAT-SERVICE.md §10): rows built under the same epoch and
/// schema stay; rows of another epoch or schema go, and are replayed from store sequence 1 or, when the runtime resumes
/// further on, rebuilt from the history.
/// </summary>
public sealed class CatchUpTests
{
    private static readonly Hash32 Epoch = Facts.Id(0xe1);
    private static readonly Hash32 Another = Facts.Id(0xe2);

    // §9.3: same epoch and schema keep; another epoch or the runtime's discard drop and replay from 1; another schema, or an
    // index without its meta row while the runtime resumes past 1, drop and rebuild.
    [Theory]
    [InlineData(1, 0xe1, false, 41ul, "Keep")] // the same epoch and schema: resume
    [InlineData(0, 0x00, false, 1ul, "Keep")] // a fresh index and a fresh cursor: the replay fills it
    [InlineData(1, 0xe2, true, 1ul, "Discard")] // another epoch: the runtime replays from 1
    [InlineData(1, 0xe2, false, 41ul, "DiscardAndRebuild")] // another epoch while the cursor resumes: rebuild from the history
    [InlineData(2, 0xe1, false, 41ul, "DiscardAndRebuild")] // another schema
    [InlineData(0, 0x00, false, 41ul, "DiscardAndRebuild")] // rows lost while the cursor kept going
    public void RowsOfAnotherEpochOrSchemaGo_ReplayedOrRebuilt(int schema, byte indexedEpoch, bool runtimeDiscards, ulong fromSeq, string expected)
    {
        Hash32 indexed = indexedEpoch == 0 ? default : indexedEpoch == 0xe1 ? Epoch : Another;
        var runtime = new IndexCatchUp { DiscardIndex = runtimeDiscards, FromSeq = fromSeq };

        Assert.Equal(System.Enum.Parse<IndexAction>(expected), CatchUpPlan.Decide(schema, indexed, runtime, Epoch));
    }
}
