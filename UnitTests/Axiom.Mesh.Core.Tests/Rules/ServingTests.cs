using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Filter = Axiom.Mesh.Local.Filter;
using GetPackage = Axiom.Mesh.Local.GetPackage;
using Grant = Axiom.Mesh.Local.Grant;
using ListGrants = Axiom.Mesh.Local.ListGrants;
using LocalClient = Axiom.Mesh.Local.LocalClient;
using LocalPackageChanged = Axiom.Mesh.Local.PackageChanged;
using RevokeGrant = Axiom.Mesh.Local.RevokeGrant;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// What a node sends a peer and takes from it (SPEC.md §7.5–§7.9, §9.7, §9.8): every Want answered, the 64-outstanding
/// bound, the 8 MiB object queue with deferral, objects checked against their hash and dropped when unwanted, nothing of
/// a Package served to a user who may not see it, the revocation itself excepted, and Rosters only as §9.8 allows.
/// </summary>
public sealed class ServingTests
{
    private const int MebibyteObject = Limits.ChunkLength - 2; // Chunk data that makes an object of exactly 1 MiB

    private static readonly CancellationToken None = CancellationToken.None;

    // §7.5: a Want of a held, visible Entry and of an unknown hash gets an Object and a NotFound; a Want that would put a
    // 65th hash outstanding (the first 64 still unsent, the peer reads nothing) is a violation.
    [Fact]
    public async Task EveryWantIsAnsweredObjectOrNotFoundUnlessCancelled_AndA65thOutstandingIsAViolation()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestNode alice = mesh["alice-laptop"];
        PackageId photos = await alice.CreatePackageAsync("Photos", PackageKind.Archive, Retention.Default, None);
        await alice.GrantAsync(photos, "bob", GrantRights.Read, None);
        TreeFixture folder = ObjectFixtures.Folder([("big", ObjectFixtures.Content(MebibyteObject, 0)), .. Enumerable.Range(1, 63).Select(i => ($"small{i:00}", ObjectFixtures.Content(10, (byte)i)))]);
        ObjectHash version = await alice.PublishAsync(photos, folder, None);
        await using ScriptedPeer bob = await EstablishedAsync(alice, "bob-desktop");
        ObjectHash unknown = new(Hash32.Of([1, 2, 3]));

        await bob.SendAsync(new Want([version, unknown]), None);
        (ObjectHash, string)[] answers = [AnswerOf(await bob.ExpectAsync<MeshMessage>(None)), AnswerOf(await bob.ExpectAsync<MeshMessage>(None))];
        Assert.Contains((version, "Object"), answers);
        Assert.Contains((unknown, "NotFound"), answers);

        ObjectHash[] chunks = [.. folder.Chunks.Select(c => c.Hash)];
        Assert.Equal(Limits.MaxOutstandingWants, chunks.Length);
        long before = alice.Node.Facts.LastSequence;
        await bob.SendAsync(new Want(chunks), None);
        await bob.SendAsync(new Want([unknown]), None);
        await mesh.WaitForFactAsync("alice-laptop", f => f.Kind == NodeFactKind.Violation && f.Device == bob.Identity.DeviceId, before, "the 65th want counted as a violation", None);
    }

    // §7.6: past 8 MiB of queued object bytes, answers wait; NotFound and every control message pass them; a Cancel takes
    // deferred answers out and settles them; as the queue drains, deferred answers move up.
    [Fact]
    public async Task Past8MiBQueuedWantsAreDeferredWhilePingPongAndCancelStillFlow()
    {
        using var outbox = new SessionOutbox();
        ObjectHash[] wanted = [.. Enumerable.Range(0, 12).Select(i => new ObjectHash(Hash32.Of([(byte)i])))];
        Assert.True(outbox.TryAcceptWant(wanted.Length + 1));
        foreach (ObjectHash hash in wanted)
            outbox.Answer(hash, Limits.ChunkLength);

        Assert.Equal(Limits.MaxQueuedObjectBytes, outbox.QueuedObjectBytes);
        Assert.Equal(4, outbox.Deferred);

        outbox.NotFound(new ObjectHash(Hash32.Of([99])));
        outbox.Send(new Pong(7));
        Assert.IsType<NotFound>((await outbox.TakeAsync(None))!.Value.Message);
        Assert.IsType<Pong>((await outbox.TakeAsync(None))!.Value.Message);

        IReadOnlyList<ObjectHash> dropped = outbox.Cancel([.. wanted[^4..]]);
        Assert.Equal(wanted[^4..], dropped);
        Assert.Equal(0, outbox.Deferred);
        Assert.Equal(8, outbox.WantsIn);

        SessionOutbox.OutboxItem first = (await outbox.TakeAsync(None))!.Value;
        Assert.Equal(wanted[0], first.Hash);
        outbox.Done(first);
        Assert.Equal(7, outbox.WantsIn);
        Assert.Equal(Limits.MaxQueuedObjectBytes - Limits.ChunkLength, outbox.QueuedObjectBytes);
    }

    // §7.6, §12.1: an Object whose bytes are not what its hash names closes the session with Bye(Violation); an unwanted
    // Chunk is dropped, while Bob's own newer Roster, sent unsolicited, is taken.
    [Fact]
    public async Task AnObjectWhoseHashDoesNotMatchIsAViolation_AnUnwantedOneIsDroppedExceptTheSendersOwnRoster()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestNode alice = mesh["alice-laptop"];
        Chunk unwanted = Samples.Chunk(5);
        VerifiedRoster newer = TestIdentity.RosterOf("bob", 2);

        await using (ScriptedPeer bob = await EstablishedAsync(alice, "bob-desktop"))
        {
            await bob.SendAsync(ObjectMessage.From(unwanted), None);
            await bob.SendAsync(ObjectMessage.From(newer), None);
            await Waits.UntilAsync(() => alice.Node.Trust.TryGetUser(bob.Identity.UserId, out UserTrust? held) && held.Roster?.Hash == newer.Hash, "Bob's newer Roster taken");
        }

        Assert.False(await alice.Node.ContainsAsync(unwanted.Hash, None));
        await using ScriptedPeer forger = await EstablishedAsync(alice, "bob-desktop", rosterSeq: 2);
        await forger.SendAsync(ObjectMessage.FromStored(Samples.Chunk(6).Hash, unwanted.Bytes), None);
        Assert.Equal(ByeReason.Violation, (await forger.ExpectByeAsync(None)).Reason);
    }

    // §9.7, Appendix B step 7: once Alice revokes Bob, Bob's node receives the revocation Grant and keeps what it holds;
    // Alice lists Bob's devices nothing of the Channel any more and answers NotFound for its Entries.
    [Fact]
    public async Task ARevokedUserIsNoLongerListedOrServed_ButReceivesTheRevocationGrant()
    {
        await using TestMesh mesh = await Meshes.AliceAndBobAsync();
        TestNode alice = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        PackageId channel = await alice.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        await alice.GrantAsync(channel, "bob", GrantRights.Read, None);
        ObjectHash entry = await alice.WriteEntryAsync(channel, ObjectFixtures.Message("before"), None);
        await bob.FollowAsync(channel, None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await bob.WaitForEntryAsync(entry, None);

        await alice.HoldUserKeyAsync(None);
        LocalClient owner = await alice.ConnectUserAsync("app", None);
        LocalClient grantee = await bob.ConnectUserAsync("app", None);
        Axiom.Mesh.Local.Subscription<LocalPackageChanged> changes = (await grantee.SubscribeAsync<LocalPackageChanged>(Filter.Any, None)).Value;

        Grant revocation = (await owner.SendAsync(new RevokeGrant { PackageId = channel, GranteeId = bob.User }, None)).Value;

        Assert.Equal((GrantRights.None, 2ul), (revocation.Rights, revocation.Seq));
        await bob.WaitForObjectAsync(revocation.Hash, None);
        Assert.True(bob.Node.Trust.TryGetGrant(channel, bob.User, out VerifiedGrant? held) && held.Content.Rights == GrantRights.None);
        Assert.True(await bob.Node.ContainsAsync(entry, None));
        LocalPackageChanged granted = await Events.FirstAsync(changes, e => e.Change == Axiom.Mesh.Local.PackageChange.Grant && e.Grant!.Hash == revocation.Hash);
        Assert.Equal(revocation.Hash, granted.Grant!.Hash);
        Assert.Equal(GrantRights.None, (await grantee.SendAsync(new GetPackage { PackageId = channel }, None)).Value.Rights);
        Assert.Equal(GrantRights.None, Assert.Single((await grantee.SendAsync(new ListGrants { PackageId = channel }, None)).Value).Rights);
        await mesh.StopNodeAsync("bob-desktop", None);
        await using ScriptedPeer scripted = await ScriptedPeer.ConnectAsync(alice, bob.Identity, ScriptedPeerOptions.Default, None);
        await scripted.SendAsync(scripted.Hello(1), None);
        await scripted.ExpectAsync<Hello>(None);
        Assert.Empty((await scripted.ExpectAsync<Packages>(None)).States);
        await scripted.SendAsync(new Want([entry]), None);
        Assert.Equal(entry, (await scripted.ExpectAsync<NotFound>(None)).Hash);
    }

    // §9.7: a Chunk is visible through any visible Package whose Entries reference it: one referenced from Bob's Archive
    // only, or from it and another, is served; one only another Archive references, or none, is NotFound.
    [Fact]
    public async Task AChunkIsServedThroughAnyVisiblePackageThatReferencesIt_ElseNotFound()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestNode alice = mesh["alice-laptop"];
        PackageId shared = await alice.CreatePackageAsync("Shared", PackageKind.Archive, Retention.Default, None);
        PackageId privately = await alice.CreatePackageAsync("Private", PackageKind.Archive, Retention.Default, None);
        await alice.GrantAsync(shared, "bob", GrantRights.Read, None);
        byte[] both = ObjectFixtures.Content(100, 1);
        TreeFixture seen = ObjectFixtures.Folder(("both.txt", both), ("seen.txt", ObjectFixtures.Content(100, 2)));
        TreeFixture hidden = ObjectFixtures.Folder(("both.txt", both), ("hidden.txt", ObjectFixtures.Content(100, 3)));
        await alice.PublishAsync(shared, seen, None);
        await alice.PublishAsync(privately, hidden, None);
        Chunk loose = Samples.Chunk(9);
        Assert.True(await alice.Node.PutAsync(loose, None));
        ObjectHash inBoth = seen.Chunks.Single(c => hidden.Chunks.Any(h => h.Hash == c.Hash)).Hash;
        ObjectHash onlySeen = seen.Chunks.Single(c => c.Hash != inBoth).Hash;
        ObjectHash onlyHidden = hidden.Chunks.Single(c => c.Hash != inBoth).Hash;
        await using ScriptedPeer bob = await EstablishedAsync(alice, "bob-desktop");

        await bob.SendAsync(new Want([inBoth, onlySeen, onlyHidden, loose.Hash]), None);
        var answers = new Dictionary<ObjectHash, string>();
        for (int i = 0; i < 4; i++)
        {
            (ObjectHash hash, string kind) = AnswerOf(await bob.ExpectAsync<MeshMessage>(None));
            answers[hash] = kind;
        }

        Assert.Equal("Object", answers[inBoth]);
        Assert.Equal("Object", answers[onlySeen]);
        Assert.Equal("NotFound", answers[onlyHidden]);
        Assert.Equal("NotFound", answers[loose.Hash]);
    }

    // §9.8: Alice's node knows Eve, but nothing Bob may see involves her: Bob's query for Eve's Roster is RosterNotFound,
    // his query for Alice's is answered with it.
    [Fact]
    public async Task ARosterQueryForAnUnrelatedUserIsRosterNotFound()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestNode alice = mesh["alice-laptop"];
        await alice.Node.AcceptUserAsync(TestIdentity.RosterOf("eve"), None);
        await using ScriptedPeer bob = await EstablishedAsync(alice, "bob-desktop");
        UserId eve = TestIdentity.RosterOf("eve").Content.User;

        await bob.SendAsync(new RosterQuery(eve), None);
        await bob.SendAsync(new RosterQuery(alice.User), None);

        Assert.Equal(eve, (await bob.ExpectAsync<RosterNotFound>(None)).User);
        Assert.Equal(TestIdentity.RosterOf("alice").Hash, (await bob.ExpectAsync<ObjectMessage>(None)).Hash);
    }

    // What an answer to Want says, read before the next read makes a received Object stale.
    private static (ObjectHash Hash, string Kind) AnswerOf(MeshMessage answer) => answer switch
    {
        ObjectMessage served => (served.Hash, "Object"),
        NotFound notFound => (notFound.Hash, "NotFound"),
        _ => (default, answer.GetType().Name),
    };

    // A scripted device of the given name, established with the node, its first listing read.
    private static async Task<ScriptedPeer> EstablishedAsync(TestNode node, string device, ulong rosterSeq = 1)
    {
        ScriptedPeer peer = await ScriptedPeer.ConnectAsync(node, TestIdentity.Of(device), ScriptedPeerOptions.Default, None);
        await peer.SendAsync(peer.Hello(rosterSeq), None);
        await peer.ExpectAsync<Hello>(None);
        await peer.ExpectAsync<Packages>(None);
        return peer;
    }
}
