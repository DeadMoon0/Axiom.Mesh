using System.Threading.Tasks;
using Axiom.Mesh.Local.Tests.Support;

namespace Axiom.Mesh.Local.Tests.Rules;

/// <summary>
/// The store epoch (LOCAL-SPEC.md §9.3): store sequences mean something only within one epoch; a client that indexed
/// under another epoch discards its index and catches up from store sequence 1.
/// </summary>
public sealed class StoreEpochTests
{
    [Fact]
    public async Task AClientDiscardsItsIndexWhenHelloAckStoreEpochDiffersFromTheOneItIndexedUnder()
    {
        (LocalClient client, ScriptedHost host) = await ScriptedHost.ConnectAsync();
        await using (host)
        await using (client)
        {
            Hash32 hostEpoch = client.Host.StoreEpoch;

            IndexCatchUp sameEpoch = IndexCatchUp.Plan(hostEpoch, lastIndexedSeq: 4_100, hostEpoch);
            Assert.False(sameEpoch.DiscardIndex);
            Assert.Equal(4_101UL, sameEpoch.FromSeq);

            IndexCatchUp otherEpoch = IndexCatchUp.Plan(Samples.Hash(200), lastIndexedSeq: 4_100, hostEpoch);
            Assert.True(otherEpoch.DiscardIndex);
            Assert.Equal(1UL, otherEpoch.FromSeq);

            IndexCatchUp noIndex = IndexCatchUp.Plan(default, lastIndexedSeq: 0, hostEpoch);
            Assert.False(noIndex.DiscardIndex);
            Assert.Equal(1UL, noIndex.FromSeq);
        }
    }
}
