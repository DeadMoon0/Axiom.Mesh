using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Protocol;
using WireState = Axiom.Mesh.Protocol.PackageState;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// Content that is well-formed but wrong (SPEC.md §8.6, §12.2): an Entry whose held parents fail the clock or Package
/// rules is dropped as faulty, recorded against the device that sent it, never stored and never forwarded, while the
/// session goes on. And the frame clock of §5.1: a frame begun and not completed within 30 seconds closes the session.
/// </summary>
public sealed class FaultyContentTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §8.6: Alice's phone, scripted, offers an Entry whose clock is 2^32 + 1 above its parent's. The laptop wants it as a
    // Head of an owner's device, finds it faulty once it holds the parent, drops it, counts it against the phone, and Bob,
    // established with the laptop, never sees it.
    [Fact]
    public async Task AForgedClockMoreThan2Pow32AboveItsParentIsDroppedAsFaultyAndNeverForwarded()
    {
        await using TestMesh mesh = await Meshes.AliceAndBobAsync();
        TestNode laptop = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        PackageId channel = await laptop.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        await laptop.GrantAsync(channel, "bob", GrantRights.Read, None);
        ObjectHash parent = await laptop.WriteEntryAsync(channel, ObjectFixtures.Message("genuine"), None);
        await bob.FollowAsync(channel, None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await bob.WaitForEntryAsync(parent, None);
        VerifiedEntry forged = Samples.Message(TestKeys.AlicePhone, channel, Limits.MaxClockGap + 1, EntryParents.Of(parent), "from the far future");

        await OfferAsync(laptop, channel, forged);

        Assert.False(await laptop.Node.ContainsAsync(forged.Hash, None));
        Assert.Equal(1, laptop.Node.Table.Find(TestIdentity.Of("alice-phone").DeviceId)!.Faulty);
        await mesh.SettleAsync(None);
        Assert.False(await bob.Node.ContainsAsync(forged.Hash, None));
    }

    // §8.6: an Entry of the Channel that names as its parent an Entry of another Package the laptop holds is faulty.
    [Fact]
    public async Task AnEntryWhoseParentIsInAnotherPackageIsFaulty()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        TestNode laptop = mesh["alice-laptop"];
        PackageId channel = await laptop.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        PackageId other = await laptop.CreatePackageAsync("Work", PackageKind.Channel, Retention.Default, None);
        ObjectHash elsewhere = await laptop.WriteEntryAsync(other, ObjectFixtures.Message("in another Package"), None);
        VerifiedEntry crossed = Samples.Message(TestKeys.AlicePhone, channel, 1, EntryParents.Of(elsewhere), "crossing Packages");

        await OfferAsync(laptop, channel, crossed);

        Assert.False(await laptop.Node.ContainsAsync(crossed.Hash, None));
    }

    // §5.1: a scripted peer begins a frame and stops after six of its bytes; 30 seconds later the node closes the session
    // with Bye(Violation) and counts it.
    [Fact]
    public async Task AFrameNotCompletedWithin30SecondsClosesTheSession()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestNode alice = mesh["alice-laptop"];
        await using ScriptedPeer bob = await ScriptedPeer.ConnectAsync(alice, TestIdentity.Of("bob-desktop"), ScriptedPeerOptions.Default, None);
        await bob.SendAsync(bob.Hello(1), None);
        await bob.ExpectAsync<Hello>(None);
        await bob.ExpectAsync<Packages>(None);
        long received = alice.Node.Replication.Sessions.Single().BytesIn;
        long before = alice.Node.Facts.LastSequence;

        await bob.SendFrameStartAsync(new Ping(1), 6, None);
        await Waits.UntilAsync(() => alice.Node.Replication.Sessions.Single().BytesIn > received, "the six bytes received");
        mesh.Clock.Advance(TimeSpan.FromSeconds(Limits.FrameCompletionSeconds));

        Assert.Equal(ByeReason.Violation, (await bob.ExpectByeAsync(None)).Reason);
        await mesh.WaitForFactAsync("alice-laptop", f => f.Kind == NodeFactKind.Violation && f.Device == bob.Identity.DeviceId, before, "the late frame counted", None);
    }

    // Alice's phone, scripted, announces the Entry as its Head of the Channel, answers the node's Want with it, and waits
    // until the node records it as faulty content.
    private static async Task OfferAsync(TestNode node, PackageId channel, VerifiedEntry entry)
    {
        long before = node.Node.Facts.LastSequence;
        await using ScriptedPeer phone = await ScriptedPeer.ConnectAsync(node, TestIdentity.Of("alice-phone"), ScriptedPeerOptions.Default, None);
        await phone.SendAsync(phone.Hello(1), None);
        await phone.ExpectAsync<Hello>(None);
        await phone.ExpectAsync<Packages>(None);
        Assert.True(node.Node.Trust.TryGetManifest(channel, out VerifiedManifest? manifest));
        var head = new Head(entry.Content.Device, entry.Hash, entry.Content.Clock);
        await phone.SendAsync(new PackageChanged(new WireState(channel, manifest.Hash, manifest.Content.Seq, [], [head])), None);

        Want want = await phone.ExpectAsync<Want>(None);
        Assert.Equal([entry.Hash], want.Hashes);
        await phone.SendAsync(ObjectMessage.FromStored(entry.Hash, entry.Bytes), None);
        NodeFact faulty = await node.Node.Facts.WaitForAsync(f => f.Kind == NodeFactKind.FaultyContent, before, None).WaitAsync(TestMesh.Deadline);
        Assert.Equal(entry.Hash.Value, faulty.Subject);
        Assert.Equal(phone.Identity.DeviceId, faulty.Device);
    }
}
