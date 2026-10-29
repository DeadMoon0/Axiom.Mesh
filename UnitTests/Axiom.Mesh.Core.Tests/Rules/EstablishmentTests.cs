using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// SPEC.md §6.2 and §6.4: a session is established only by the peer's valid <c>Hello</c>; a close before it is a refusal
/// of this node and backs off like a failed dial; a node that stops says <c>Bye(Shutdown)</c>; a device that leaves the
/// node's trust is closed with <c>Bye(Unauthorized)</c> (§8.8).
/// </summary>
public sealed class EstablishmentTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly IPEndPoint BobsAddress = new(IPAddress.Parse("10.0.0.9"), 41810);

    // §6.2: Bob's desktop completes the handshake Alice dialled, then closes without its Hello. Alice's own handshake
    // completing is no acceptance: she records a refusal and holds her next dial back for 10 seconds.
    [Fact]
    public async Task ACloseAfterTheHandshakeBeforeHelloCountsAsARefusalAndBacksOff()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestIdentity bob = TestIdentity.Of("bob-desktop");

        await using (ScriptedPeer dialled = await ScriptedPeer.AcceptAsync(mesh, bob, BobsAddress, None))
            Assert.True(dialled.HandshakeCompleted, dialled.HandshakeFailure?.ToString());

        NodeFact backedOff = await mesh.WaitForFactAsync("alice-laptop", f => f.Kind == NodeFactKind.BackedOff && f.Device == bob.DeviceId, 0, "the refusal backed off", None);
        PeerRecord record = Assert.Single(mesh["alice-laptop"].Node.Peers, p => p.Device == bob.DeviceId);
        Assert.Equal(PeerState.Refused, record.State);
        Assert.Equal(1, record.Failures);
        Assert.Equal(backedOff.At + TimeSpan.FromSeconds(10), record.BackoffUntil);
        Assert.Contains(mesh["alice-laptop"].Node.Facts.Recent(), f => f.Kind == NodeFactKind.RefusedBeforeHello && f.Device == bob.DeviceId);
    }

    // §6.4: a stopping node ends its sessions with Bye(Shutdown).
    [Fact]
    public async Task AStoppingNodeSaysByeShutdownToItsPeers()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        await using ScriptedPeer bob = await ScriptedPeer.ConnectAsync(mesh["alice-laptop"], TestIdentity.Of("bob-desktop"), ScriptedPeerOptions.Default, None);
        await bob.SendAsync(bob.Hello(1), None);
        await bob.ExpectAsync<Hello>(None);
        await mesh.WaitForFactAsync("alice-laptop", f => f.Kind == NodeFactKind.Established, 0, "Bob established", None);

        Task stopping = mesh.StopNodeAsync("alice-laptop", None);

        Bye bye = await bob.ExpectByeAsync(None);
        Assert.Equal(ByeReason.Shutdown, bye.Reason);
        await stopping;
    }

    // §8.8, §4.3: when Bob's newer Roster removes his desktop, Alice closes its established session with
    // Bye(Unauthorized) and refuses the key from then on.
    [Fact]
    public async Task AnEstablishedSessionOfADeviceRemovedLaterIsClosedWithByeUnauthorized()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        await using ScriptedPeer bob = await ScriptedPeer.ConnectAsync(mesh["alice-laptop"], TestIdentity.Of("bob-desktop"), ScriptedPeerOptions.Default, None);
        await bob.SendAsync(bob.Hello(1), None);
        await bob.ExpectAsync<Hello>(None);
        await mesh.WaitForFactAsync("alice-laptop", f => f.Kind == NodeFactKind.Established, 0, "Bob established", None);

        await mesh["alice-laptop"].Node.AcceptUserAsync(TestIdentity.RosterOf("bob", 2, "bob-desktop"), None);

        Bye bye = await bob.ExpectByeAsync(None);
        Assert.Equal(ByeReason.Unauthorized, bye.Reason);
        await mesh.WaitUntilAsync("alice-laptop", n => n.Peers.Single().Sessions == 0, "the session ended", None);
        await using ScriptedPeer again = await ScriptedPeer.ConnectAsync(mesh["alice-laptop"], TestIdentity.Of("bob-desktop"), ScriptedPeerOptions.Default with { From = new IPEndPoint(IPAddress.Parse("10.0.0.201"), 50000) }, None);
        await again.ExpectClosedWithoutHelloAsync(None);
    }
}
