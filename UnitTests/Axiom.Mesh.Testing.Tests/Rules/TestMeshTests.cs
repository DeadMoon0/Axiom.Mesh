using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;

namespace Axiom.Mesh.Testing.Tests.Rules;

/// <summary>
/// The harness's promises: nodes whose users know each other connect over real mutual TLS and nothing else does; a
/// stopped node is gone and a restarted one comes back with what it kept; a partition cuts and a heal rejoins.
/// </summary>
public sealed class TestMeshTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static Task<TestMesh> AliceAndBobAsync() =>
        TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop", "bob-desktop") with { Trust = TrustSeed.MutuallyKnown("alice-laptop", "bob-desktop") }, None);

    [Fact]
    public async Task TwoNodesWhoseUsersKnowEachOtherConnect_OneSessionEach()
    {
        await using TestMesh mesh = await AliceAndBobAsync();

        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);

        PeerRecord bob = Assert.Single(mesh["alice-laptop"].Node.Peers, p => p.Device == mesh["bob-desktop"].Device);
        Assert.Equal(PeerState.Established, bob.State);
        Assert.Equal(1, bob.Sessions);
        Assert.Equal(mesh["alice-laptop"].Device, mesh["alice-laptop"].Node.Identity!.Device);
        Assert.Equal(mesh["alice-laptop"].User, mesh["alice-laptop"].Node.Identity!.User);
    }

    // Alice's laptop and phone share a user and connect; Bob's desktop, whose user neither knows, is never dialled.
    [Fact]
    public async Task OnlyNodesWhoseUsersKnowEachOtherConnect()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop", "alice-phone", "bob-desktop"), None);

        await mesh.ConnectAsync("alice-laptop", "alice-phone", None);

        Assert.DoesNotContain(mesh["alice-laptop"].Node.Peers, p => p.Device == mesh["bob-desktop"].Device);
        Assert.DoesNotContain(mesh["alice-phone"].Node.Peers, p => p.Device == mesh["bob-desktop"].Device);
        Assert.Empty(mesh["bob-desktop"].Node.Peers);
    }

    // A stopped node closes its sessions; started again on what it kept, it is the same identity and reconnects.
    [Fact]
    public async Task AStoppedNodeIsGone_AndARestartedOneComesBackWithItsIdentityAndReconnects()
    {
        await using TestMesh mesh = await AliceAndBobAsync();
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);

        await mesh.StopNodeAsync("bob-desktop", None);
        await mesh.WaitUntilAsync("alice-laptop", n => n.Peers.Single().Sessions == 0, "Bob's session ended", None);
        Assert.False(mesh["bob-desktop"].IsRunning);

        await mesh.StartNodeAsync("bob-desktop", None);
        Assert.Equal(mesh["bob-desktop"].Device, mesh["bob-desktop"].Node.Identity!.Device);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
    }

    // A partition cuts the session (and any dial across it, which then backs off); healing lets the nodes hear each other
    // again, and they redial once the clock reaches the end of any backoff. The clock is moved to exactly that moment, so
    // a handshake the retry starts is not expired by the same move.
    [Fact]
    public async Task APartitionCutsTheSession_AndHealingReconnects()
    {
        await using TestMesh mesh = await AliceAndBobAsync();
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);

        mesh.Partition("alice-laptop");
        await mesh.WaitUntilAsync("alice-laptop", n => n.Peers.Single().Sessions == 0, "the cut session ended", None);
        await mesh.WaitUntilAsync("bob-desktop", n => n.Peers.Single().Sessions == 0, "the cut session ended", None);

        mesh.Heal();
        DateTimeOffset? due = new[] { "alice-laptop", "bob-desktop" }.SelectMany(n => mesh[n].Node.Peers).Max(p => p.BackoffUntil);
        if (due is { } until)
            mesh.Clock.Advance(until - mesh.Clock.GetUtcNow());
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
    }

    // Every published device runs as itself, and its user's first Roster lists it: what a mesh adopts as identities.
    [Fact]
    public void EveryPublishedDeviceIsListedInItsUsersFirstRoster()
    {
        foreach (TestIdentity identity in TestIdentity.All)
        {
            VerifiedRoster roster = TestIdentity.RosterOf(identity.UserName);
            Assert.Same(roster, TestIdentity.RosterOf(identity.UserName));
            Assert.Equal(identity.UserId, roster.Content.User);
            Assert.True(roster.Content.TryGetDevice(identity.DeviceId, out RosterDevice? listed));
            Assert.False(listed.Removed);
        }
    }
}
