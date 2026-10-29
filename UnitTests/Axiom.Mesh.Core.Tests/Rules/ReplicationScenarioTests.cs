using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// Replication end to end between in-memory nodes on one fake clock (SPEC.md §10, Appendix B; plan §5 scenarios "follow
/// and replicate incl. 3-node relay", "offline/online"): what one node writes, a node that may see it and follows it
/// comes to hold, Entries, Trees and Chunks alike, through a relay and after being offline, without polling.
/// </summary>
public sealed class ReplicationScenarioTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §10.1–§10.3: Bob follows Alice's Archive; both Versions arrive, and under the desktop row every Tree, ChunkList and
    // Chunk of the published folder too, a 2.5 MB file in three Chunks among them.
    [Fact]
    public async Task TwoNodesReplicateAPackageWithEntriesTreeAndChunks()
    {
        await using TestMesh mesh = await Meshes.AliceAndBobAsync();
        TestNode alice = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        PackageId photos = await alice.CreatePackageAsync("Photos", PackageKind.Archive, Retention.Default, None);
        await alice.GrantAsync(photos, "bob", GrantRights.Read, None);
        TreeFixture folder = ObjectFixtures.Folder(("a.jpg", ObjectFixtures.Content(2_500_000, 1)), ("b.txt", ObjectFixtures.Content(10, 2)));
        ObjectHash first = await alice.PublishAsync(photos, folder, None);
        ObjectHash second = await alice.WriteEntryAsync(photos, ObjectFixtures.Version(ObjectFixtures.Folder(("c.txt", ObjectFixtures.Content(5, 3)))), None);

        await bob.FollowAsync(photos, None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await bob.WaitForEntryAsync(second, None);
        await bob.WaitForEntryAsync(first, None);
        await mesh.SettleAsync(None);

        Assert.Equal(4, folder.Chunks.Count);
        foreach (StorableObject part in folder.Objects)
            Assert.True(await bob.Node.ContainsAsync(part.Hash, None), $"Bob lacks {part.GetType().Name} {part.Hash}.");
    }

    // Appendix B steps 3–5: the phone writes; the laptop stores it and pushes it to Bob, who has no link to the phone; the
    // laptop sends the phone nothing about the change but its Want for it (§7.3, §10.5).
    [Fact]
    public async Task AThirdNodeRelaysAndNothingIsSentBackToTheSource()
    {
        await using TestMesh mesh = await Meshes.AppendixBAsync();
        TestNode laptop = mesh["alice-laptop"];
        TestNode phone = mesh["alice-phone"];
        TestNode bob = mesh["bob-desktop"];
        PackageId channel = await laptop.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        await laptop.GrantAsync(channel, "bob", GrantRights.Read, None);
        ObjectHash e1 = await laptop.WriteEntryAsync(channel, ObjectFixtures.Message("hello"), None);
        await phone.FollowAsync(channel, None);
        await bob.FollowAsync(channel, None);
        await mesh.ConnectAsync("alice-laptop", "alice-phone", None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await phone.WaitForEntryAsync(e1, None);
        await bob.WaitForEntryAsync(e1, None);
        await mesh.SettleAsync(None);
        long handledByPhone = HandledFrom(phone, laptop);

        ObjectHash e2 = await phone.WriteEntryAsync(channel, ObjectFixtures.Message("typed on the phone"), None);

        await bob.WaitForEntryAsync(e2, None);
        await mesh.SettleAsync(None);
        Assert.Equal(1, HandledFrom(phone, laptop) - handledByPhone); // the laptop's Want for e2, nothing else
        Assert.DoesNotContain(bob.Node.Replication.Sessions, s => s.Device == phone.Device);
    }

    // §10.2 on reconnect: Bob was offline while Alice wrote twice; once the two find each other again, Bob holds both.
    [Fact]
    public async Task AFollowerThatWasOfflineCatchesUpAfterReconnect()
    {
        await using TestMesh mesh = await Meshes.AliceAndBobAsync();
        TestNode alice = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        PackageId channel = await alice.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        await alice.GrantAsync(channel, "bob", GrantRights.Read, None);
        await bob.FollowAsync(channel, None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        ObjectHash before = await alice.WriteEntryAsync(channel, ObjectFixtures.Message("before"), None);
        await bob.WaitForEntryAsync(before, None);

        await mesh.StopNodeAsync("bob-desktop", None);
        ObjectHash third = (await ObjectFixtures.WriteMessagesAsync(alice, channel, 2, None))[^1];
        await mesh.StartNodeAsync("bob-desktop", None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);

        await bob.WaitForEntryAsync(third, None);
        await mesh.SettleAsync(None);
        Assert.Equal(3, await StoredEntriesAsync(bob, channel));
    }

    // §10.5: a write on the phone reaches Bob two hops away while the clock stands still: pushed, never polled.
    [Fact]
    public async Task AChangeCrossesTwoHopsWithoutPolling()
    {
        await using TestMesh mesh = await Meshes.AppendixBAsync();
        TestNode laptop = mesh["alice-laptop"];
        TestNode phone = mesh["alice-phone"];
        TestNode bob = mesh["bob-desktop"];
        PackageId channel = await laptop.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        await laptop.GrantAsync(channel, "bob", GrantRights.Read, None);
        await phone.FollowAsync(channel, None);
        await bob.FollowAsync(channel, None);
        await mesh.ConnectAsync("alice-laptop", "alice-phone", None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await mesh.SettleAsync(None);
        DateTimeOffset now = mesh.Clock.GetUtcNow();

        ObjectHash written = await phone.WriteEntryAsync(channel, ObjectFixtures.Message("two hops"), None);

        await bob.WaitForEntryAsync(written, None);
        Assert.Equal(now, mesh.Clock.GetUtcNow());
    }

    // §11.3: once evicted, an object is not served: a Want for it is answered NotFound, while what is still stored is sent.
    [Fact]
    public async Task AnEvictedObjectIsNeverClaimedAfterwards()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestNode alice = mesh["alice-laptop"];
        PackageId photos = await alice.CreatePackageAsync("Photos", PackageKind.Archive, Retention.Default, None);
        await alice.GrantAsync(photos, "bob", GrantRights.Read, None);
        await alice.Node.Writer.SendAsync(new PackageCommand(PackageCommandKind.SetPolicy, photos, LocalRetention.LatestOnly), None);
        TreeFixture folder = ObjectFixtures.Folder(("a.txt", ObjectFixtures.Content(100, 1)));
        await alice.PublishAsync(photos, folder, None);
        Chunk chunk = folder.Chunks[0];
        await alice.Node.Writer.SendAsync(new PinCommand(PinCommandKind.Sent, [chunk.Hash], PinReason.Outbound, default), None);
        Assert.Equal(EvictionOutcome.Evicted, await alice.Node.Store.TryEvictAsync(chunk.Hash, None));

        await using ScriptedPeer bob = await ScriptedPeer.ConnectAsync(alice, TestIdentity.Of("bob-desktop"), ScriptedPeerOptions.Default, None);
        await bob.SendAsync(bob.Hello(1), None);
        await bob.ExpectAsync<Protocol.Hello>(None);
        await bob.ExpectAsync<Protocol.Packages>(None);
        await bob.SendAsync(new Protocol.Want([chunk.Hash, folder.Root.Hash]), None);

        Assert.Equal(chunk.Hash, (await bob.ExpectAsync<Protocol.NotFound>(None)).Hash);
        Assert.Equal(folder.Root.Hash, (await bob.ExpectAsync<Protocol.ObjectMessage>(None)).Hash);
    }

    private static long HandledFrom(TestNode node, TestNode peer) =>
        node.Node.Replication.Sessions.Single(s => s.Device == peer.Device).MessagesHandled;

    private static async Task<int> StoredEntriesAsync(TestNode node, PackageId package)
    {
        await using Axiom.LDB.Entity.LdbContext context = await Axiom.LDB.Entity.LdbContext.OpenAsync(node.Node.Writer.Database, None);
        return (await EntryIndex.InOrderAsync(context, package, null, 64, None)).Count;
    }
}
