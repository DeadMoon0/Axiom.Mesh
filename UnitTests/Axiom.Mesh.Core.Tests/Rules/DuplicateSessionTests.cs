using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// SPEC.md §4.5 end to end: at most one established session per device id; when a second one establishes, the dead old
/// one goes, else of two dialled by the same device the newer stays, else the one dialled by the lower device id stays;
/// the other is closed with <c>Bye(Duplicate)</c>. The device ids decide: alice-laptop is 97cb…, eve-device ad15…, so
/// between them the session Alice dialled is the one dialled by the lower id.
/// </summary>
public sealed class DuplicateSessionTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly IPEndPoint EvesAddress = new(IPAddress.Parse("10.0.0.9"), 41810);

    // Both nodes hear each other at once and both dial: whatever the timing, each keeps one session, the same connection.
    [Fact]
    public async Task BothDialAtOnce_ExactlyOneSessionPerDeviceSurvives_TheSameConnectionOnBothNodes()
    {
        await using TestMesh mesh = await Meshes.AliceAndBobAsync();

        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);

        PeerSession alicesKept = mesh["alice-laptop"].Node.Table.EstablishedWith(mesh["bob-desktop"].Device)!;
        PeerSession bobsKept = mesh["bob-desktop"].Node.Table.EstablishedWith(mesh["alice-laptop"].Device)!;
        Assert.Equal(alicesKept.DialledBy, bobsKept.DialledBy);
        Assert.NotEqual(alicesKept.IsDialledHere, bobsKept.IsDialledHere);
    }

    // Rule 2: Bob's desktop connects twice; both were dialled by it, so the newer stays and the older hears Bye(Duplicate).
    [Fact]
    public async Task TwoSessionsDialledByTheSameDevice_TheNewerIsKept()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestIdentity bob = TestIdentity.Of("bob-desktop");
        await using ScriptedPeer older = await EstablishedAsync(mesh, bob, "10.0.0.200");

        await using ScriptedPeer newer = await EstablishedAsync(mesh, bob, "10.0.0.201");

        Assert.Equal(ByeReason.Duplicate, (await older.ExpectByeAsync(None)).Reason);
        PeerSession kept = await SettledAsync(mesh, bob);
        Assert.False(kept.IsDialledHere);
        Assert.Equal(IPAddress.Parse("10.0.0.201"), kept.Remote.Address);
    }

    // Rule 3: Alice dialled Eve's device, then Eve's device dials Alice. Alice's id is the lower dialler, so the session
    // Alice dialled stays and the new one hears Bye(Duplicate).
    [Fact]
    public async Task TwoSessionsDialledByDifferentDevices_TheOneDialledByTheLowerDeviceIdIsKept()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestIdentity eve = TestIdentity.Of("eve-device");
        Assert.True(mesh["alice-laptop"].Device < eve.DeviceId);
        await mesh["alice-laptop"].Node.AcceptUserAsync(TestIdentity.RosterOf("eve"), None);
        await using ScriptedPeer dialledByAlice = await DialledByAliceAsync(mesh, eve);

        await using ScriptedPeer dialledByEve = await EstablishedAsync(mesh, eve, "10.0.0.200");

        Assert.Equal(ByeReason.Duplicate, (await dialledByEve.ExpectByeAsync(None)).Reason);
        Assert.True((await SettledAsync(mesh, eve)).IsDialledHere);
    }

    // Rule 1: as above, but the session Alice dialled has received nothing for 60 seconds: it is dead, the new one stays
    // although rule 3 would have kept the old one.
    [Fact]
    public async Task AnOldSessionSilentFor60SecondsIsDead_AndTheNewIsKept()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestIdentity eve = TestIdentity.Of("eve-device");
        await mesh["alice-laptop"].Node.AcceptUserAsync(TestIdentity.RosterOf("eve"), None);
        await using ScriptedPeer silent = await DialledByAliceAsync(mesh, eve);

        mesh.Clock.Advance(TimeSpan.FromSeconds(60));
        await using ScriptedPeer fresh = await EstablishedAsync(mesh, eve, "10.0.0.200");

        Assert.Equal(ByeReason.Duplicate, (await silent.ExpectByeAsync(None)).Reason);
        Assert.False((await SettledAsync(mesh, eve)).IsDialledHere);
    }

    // A scripted peer that connects to Alice and exchanges Hellos; returns once Alice holds it established or closed it.
    private static async Task<ScriptedPeer> EstablishedAsync(TestMesh mesh, TestIdentity identity, string from)
    {
        long before = mesh["alice-laptop"].Node.Facts.LastSequence;
        ScriptedPeer peer = await ScriptedPeer.ConnectAsync(mesh["alice-laptop"], identity, ScriptedPeerOptions.Default with { From = new IPEndPoint(IPAddress.Parse(from), 50000) }, None);
        await peer.SendAsync(peer.Hello(1), None);
        await peer.ExpectAsync<Hello>(None);
        await mesh.WaitForFactAsync("alice-laptop", f => f.Kind is NodeFactKind.Established or NodeFactKind.DuplicateClosed && f.Device == identity.DeviceId, before, $"Alice's decision on {identity.Name} from {from}", None);
        return peer;
    }

    // A scripted peer Alice dials; it sends its Hello raw and nothing after, not even a heartbeat.
    private static async Task<ScriptedPeer> DialledByAliceAsync(TestMesh mesh, TestIdentity identity)
    {
        ScriptedPeer peer = await ScriptedPeer.AcceptAsync(mesh, identity, EvesAddress, None);
        await peer.SendRawAsync(peer.Hello(1), None);
        await mesh.WaitForFactAsync("alice-laptop", f => f.Kind == NodeFactKind.Established && f.Device == identity.DeviceId, 0, $"Alice established with {identity.Name}", None);
        return peer;
    }

    private static async Task<PeerSession> SettledAsync(TestMesh mesh, TestIdentity identity)
    {
        await mesh.WaitUntilAsync("alice-laptop", n => n.Peers.Single(p => p.Device == identity.DeviceId) is { State: PeerState.Established, Sessions: 1 }, "one session left", None);
        return mesh["alice-laptop"].Node.Table.EstablishedWith(identity.DeviceId)!;
    }
}
