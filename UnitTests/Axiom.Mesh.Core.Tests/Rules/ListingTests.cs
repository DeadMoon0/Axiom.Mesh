using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Protocol;
using WireState = Axiom.Mesh.Protocol.PackageState;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// The listing of SPEC.md §7.2 and §9.7: exactly the Packages the peer's user owns or holds a current Read or Write Grant
/// for, with every Grant to the owner's devices and only the peer's own to anyone else; the rules a listing must keep,
/// each broken one a violation; and an owner's listing that omits a Package withdrawing the Grant.
/// </summary>
public sealed class ListingTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // Alice owns four Packages: one granted to Bob for reading, one for writing, one not granted, one revoked. Bob's device
    // is listed the first two, each with Bob's Grant alone; Alice's phone is listed all four, with every Grant.
    [Fact]
    public async Task TheListingHoldsExactlyThePackagesWhoseOwnerOrCurrentGranteeIsThePeersUser()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestNode alice = mesh["alice-laptop"];
        PackageId read = await alice.CreatePackageAsync("Read", PackageKind.Channel, Retention.Default, None);
        PackageId write = await alice.CreatePackageAsync("Write", PackageKind.Channel, Retention.Default, None);
        PackageId own = await alice.CreatePackageAsync("Own", PackageKind.Channel, Retention.Default, None);
        PackageId revoked = await alice.CreatePackageAsync("Revoked", PackageKind.Channel, Retention.Default, None);
        VerifiedGrant readGrant = await alice.GrantAsync(read, "bob", GrantRights.Read, None);
        VerifiedGrant writeGrant = await alice.GrantAsync(write, "bob", GrantRights.Write, None);
        await alice.GrantAsync(revoked, "bob", GrantRights.Read, None);
        VerifiedGrant revocation = await alice.GrantAsync(revoked, "bob", GrantRights.None, None);

        WireState[] toBob = await ListingSeenByAsync(alice, "bob-desktop");
        WireState[] toPhone = await ListingSeenByAsync(alice, "alice-phone");

        Assert.Equal(new[] { read, write }.Order(), toBob.Select(s => s.Package).Order());
        Assert.Equal([readGrant.Hash], toBob.Single(s => s.Package == read).Grants);
        Assert.Equal([writeGrant.Hash], toBob.Single(s => s.Package == write).Grants);
        Assert.Equal(new[] { read, write, own, revoked }.Order(), toPhone.Select(s => s.Package).Order());
        Assert.Equal([revocation.Hash], toPhone.Single(s => s.Package == revoked).Grants);
        Assert.Empty(toPhone.Single(s => s.Package == own).Grants);
    }

    // §7.2, §12.1: each listing rule broken by a scripted peer closes the session with Bye(Violation) and counts it.
    [Theory]
    [InlineData("page 1 first")] // pages are numbered from 0 consecutively
    [InlineData("65 537 states")] // a listing holds at most 65 536 PackageStates
    [InlineData("61 seconds")] // a listing completes within 60 seconds of its page 0
    [InlineData("PackageChanged between pages")] // nothing but other messages between the pages of a listing
    public async Task APageOutOfSequence_AnOver65536Listing_OneSlowerThan60s_OrAPackageChangedMidListing_IsAViolation(string broken)
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestNode alice = mesh["alice-laptop"];
        await using ScriptedPeer bob = await ScriptedPeer.ConnectAsync(alice, TestIdentity.Of("bob-desktop"), ScriptedPeerOptions.Default, None);
        await bob.SendAsync(bob.Hello(1), None);
        await bob.ExpectAsync<Hello>(None);
        await bob.ExpectAsync<Packages>(None);
        WireState[] page = [.. Enumerable.Range(0, Limits.MaxPackagesPerPage).Select(i => State((byte)i))];
        long before = alice.Node.Facts.LastSequence;

        switch (broken)
        {
            case "page 1 first":
                await bob.SendAsync(new Packages(1, true, []), None);
                break;
            case "65 537 states":
                for (int at = 0; at <= Limits.MaxListingPackages / Limits.MaxPackagesPerPage; at++)
                    await bob.SendAsync(new Packages((ushort)at, false, page), None);
                break;
            case "61 seconds":
                await bob.SendAsync(new Packages(0, false, page), None);
                await Waits.UntilAsync(() => alice.Node.Replication.Sessions.Single().MessagesHandled >= 1, "page 0 handled");
                // A Want answered just before the deadline keeps both sides' heartbeats quiet across it: otherwise the node's
                // Ping arrives ahead of its Bye, and the scripted peer's automatic Pong can find the connection already
                // closed (the in-memory transport refuses a write to a closed end; TCP would buffer it).
                mesh.Clock.Advance(TimeSpan.FromSeconds(Limits.ListingSeconds - 1));
                await bob.SendAsync(new Want([page[0].ManifestHash]), None);
                while (await bob.ExpectAsync<MeshMessage>(None) is not NotFound)
                {
                    // The node's own Wants for the listed Manifests come first.
                }
                mesh.Clock.Advance(TimeSpan.FromSeconds(2));
                break;
            default:
                await bob.SendAsync(new Packages(0, false, page), None);
                await bob.SendAsync(new PackageChanged(page[0]), None);
                break;
        }

        Bye bye = await bob.ExpectByeAsync(None);
        Assert.Equal(ByeReason.Violation, bye.Reason);
        await mesh.WaitForFactAsync("alice-laptop", f => f.Kind == NodeFactKind.Violation && f.Device == bob.Identity.DeviceId, before, "the listing violation counted", None);
    }

    // §7.2: Bob's node holds a Grant to Alice's Channel; a device of Alice that listed the Channel and then omits it
    // withdraws the Grant, so Bob's node no longer sees itself in the Channel's audience, until a newer Grant arrives.
    [Fact]
    public async Task AnOwnersListingThatOmitsAPackageWithdrawsTheGrantUntilANewerGrantProvesOtherwise()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("bob-desktop"), None);
        TestNode bob = mesh["bob-desktop"];
        await bob.Node.AcceptUserAsync(TestIdentity.RosterOf("alice"), None);
        VerifiedManifest channel = Samples.Channel(TestKeys.Alice);
        PackageId package = channel.Content.Package;
        await bob.FollowAsync(package, None);
        Assert.True(await bob.Node.PutAsync(channel, None));
        VerifiedGrant first = Samples.Grant(TestKeys.Alice, channel, TestKeys.Bob, GrantRights.Read, seq: 1);
        Assert.True(await bob.Node.PutAsync(first, None));
        Assert.True(SeesItself(bob, package));

        await using ScriptedPeer alice = await ScriptedPeer.ConnectAsync(bob, TestIdentity.Of("alice-laptop"), ScriptedPeerOptions.Default, None);
        await alice.SendAsync(alice.Hello(1), None);
        await alice.ExpectAsync<Hello>(None);
        await alice.SendAsync(new Packages(0, true, [new WireState(package, channel.Hash, 1, [first.Hash], [])]), None);
        await alice.SendAsync(new Packages(0, true, []), None);
        await Waits.UntilAsync(() => !SeesItself(bob, package), "the Grant withdrawn");

        Assert.True(await bob.Node.PutAsync(Samples.Grant(TestKeys.Alice, channel, TestKeys.Bob, GrantRights.Read, seq: 2), None));
        Assert.True(SeesItself(bob, package));
    }

    private static bool SeesItself(TestNode node, PackageId package) =>
        node.Node.Trust.TryGetPackage(package, out PackageTrust? held) && held.Audience is { } audience && audience.Includes(node.User);

    // The first listing a scripted device of that name gets from the node.
    private static async Task<WireState[]> ListingSeenByAsync(TestNode node, string device)
    {
        await using ScriptedPeer peer = await ScriptedPeer.ConnectAsync(node, TestIdentity.Of(device), ScriptedPeerOptions.Default, None);
        await peer.SendAsync(peer.Hello(1), None);
        await peer.ExpectAsync<Hello>(None);
        Packages page = await peer.ExpectAsync<Packages>(None);
        Assert.Equal(0, page.Page);
        Assert.True(page.Last);
        return [.. page.States];
    }

    private static WireState State(byte fill) =>
        new(new PackageId(new Hash32(Enumerable.Repeat(fill, Hash32.Length).ToArray())), new ObjectHash(Hash32.Of([fill])), 1, [], []);
}
