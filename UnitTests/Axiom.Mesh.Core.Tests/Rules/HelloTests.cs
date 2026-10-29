using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// SPEC.md §6.1 and §7.1: each side sends <c>Hello</c> first; the node checks the peer's against the TLS key and its
/// current Rosters, refuses a failing one with <c>Bye(Unauthorized)</c>, and follows a valid one with the Roster the peer
/// lacks or a query for the one it has.
/// </summary>
public sealed class HelloTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Theory]
    [InlineData("alice-phone", "bob")] // deviceId names another device than the one whose key the handshake proved
    [InlineData("bob-desktop", "alice")] // the device is in Bob's Roster, but the Hello names Alice as its user
    [InlineData("bob-desktop", "eve")] // the Hello names a user the node does not know at all
    public async Task ADeviceIdThatIsNotTheHashOfTheTlsKeyOrNotInTheUsersCurrentRosterIsByeUnauthorized(string claimedDevice, string claimedUser)
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        await using ScriptedPeer bob = await ScriptedPeer.ConnectAsync(mesh["alice-laptop"], TestIdentity.Of("bob-desktop"), ScriptedPeerOptions.Default, None);
        TestIdentity device = TestIdentity.Of(claimedDevice);
        UserId user = TestIdentity.RosterOf(claimedUser).Content.User;

        await bob.SendAsync(new Hello(device.DeviceId, user, 1, Capabilities.None, []), None);

        Hello theirs = await bob.ExpectAsync<Hello>(None);
        Bye bye = await bob.ExpectAsync<Bye>(None);
        Assert.Equal(mesh["alice-laptop"].Device, theirs.Device);
        Assert.Equal(ByeReason.Unauthorized, bye.Reason);
        await mesh.WaitForFactAsync("alice-laptop", f => f.Kind == NodeFactKind.Violation && f.Device == bob.Identity.DeviceId, 0, "the refused Hello counted", None);
        Assert.DoesNotContain(mesh["alice-laptop"].Node.Facts.Recent(), f => f.Kind == NodeFactKind.Established);
    }

    // §7.1: the node holds Bob's Roster seq 1; a peer claiming seq 0 gets it as an unsolicited Object right after the
    // node's Hello, and a peer claiming seq 2 gets a RosterQuery for its user.
    [Fact]
    public async Task APeerHoldingAnOlderRosterGetsTheHeldOneAfterHello_ANewerOneIsQueried()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestIdentity desktop = TestIdentity.Of("bob-desktop");

        await using (ScriptedPeer older = await ScriptedPeer.ConnectAsync(mesh["alice-laptop"], desktop, ScriptedPeerOptions.Default, None))
        {
            await older.SendAsync(older.Hello(0), None);
            await older.ExpectAsync<Hello>(None);
            ObjectMessage roster = await older.ExpectAsync<ObjectMessage>(None);
            Assert.Equal(TestIdentity.RosterOf("bob").Hash, roster.Hash);
        }

        await mesh.WaitUntilAsync("alice-laptop", n => n.Peers.Count == 1 && n.Peers[0].Sessions == 0, "the first session ended", None);
        await using ScriptedPeer newer = await ScriptedPeer.ConnectAsync(mesh["alice-laptop"], desktop, ScriptedPeerOptions.Default, None);
        await newer.SendAsync(newer.Hello(2), None);
        await newer.ExpectAsync<Hello>(None);
        RosterQuery query = await newer.ExpectAsync<RosterQuery>(None);
        Assert.Equal(desktop.UserId, query.User);
    }
}
