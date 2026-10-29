using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §4.2 and §5.6 over the local port: <c>HelloAck.storeSeq</c> is the store sequence of the newest stored
/// Entry, 0 before the first; the first Entry stored is 1, every next one one higher, and the sequence and the epoch
/// survive a restart of the node.
/// </summary>
public sealed class HelloAckTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task TheFirstEntryStoredIs1ThenPlusOne_AndHelloAckCarriesTheNewestAcrossARestart()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        LocalClient before = await node.ConnectUserAsync("index", None);
        Assert.Equal(0ul, before.Host.StoreSeq);
        Subscription<EntryStored> stored = (await before.SubscribeAsync<EntryStored>(Filter.Any, None)).Value;

        PackageId channel = await node.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        for (int i = 0; i < 3; i++)
            await node.WriteEntryAsync(channel, ObjectFixtures.Message("hello " + i), None);

        var seqs = new System.Collections.Generic.List<ulong>();
        using (var deadline = new CancellationTokenSource(TestMesh.Deadline))
        {
            await foreach (EntryStored entry in stored.WithCancellation(deadline.Token))
            {
                seqs.Add(entry.Entry.StoreSeq);
                if (seqs.Count == 3)
                    break;
            }
        }

        Assert.Equal([1ul, 2, 3], seqs);
        Hash32 epoch = before.Host.StoreEpoch;
        await mesh.StopNodeAsync(LocalMeshes.Node, None);
        await mesh.StartNodeAsync(LocalMeshes.Node, None);

        LocalClient after = await node.ConnectUserAsync("index", None);
        Assert.Equal(3ul, after.Host.StoreSeq);
        Assert.Equal(epoch, after.Host.StoreEpoch);
        await node.WriteEntryAsync(channel, ObjectFixtures.Message("after the restart"), None);
        LocalClient later = await node.ConnectUserAsync("index", None);
        Assert.Equal(4ul, later.Host.StoreSeq);
    }
}
