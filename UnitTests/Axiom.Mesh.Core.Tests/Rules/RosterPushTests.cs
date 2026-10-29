using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// Rosters between established nodes (SPEC.md §7.1, §7.6, §7.9): a peer holding an older Roster of its own user gets the
/// newer one unsolicited after <c>Hello</c> and takes it; a peer holding a newer one is asked for it and answers; and a
/// user's new Roster goes to every established peer the moment its seq rises.
/// </summary>
public sealed class RosterPushTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §7.1, §7.6: Alice holds Bob's Roster seq 2, Bob's own node seq 1: Bob gets seq 2 unsolicited and takes it. Then the
    // other way: Bob's node holds seq 3 and Alice seq 2: Alice queries Bob's Roster and takes the answer.
    [Fact]
    public async Task ALowerRosterSeqGetsTheRosterAsAnUnsolicitedObject_AHigherOneGetsRosterQuery()
    {
        await using TestMesh mesh = await Meshes.AliceAndBobAsync();
        TestNode alice = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        VerifiedRoster second = TestIdentity.RosterOf("bob", 2);
        await alice.Node.AcceptUserAsync(second, None);

        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await mesh.WaitUntilAsync("bob-desktop", _ => HoldsRoster(bob, bob.User, second), "Bob's node takes its own newer Roster from Alice", None);

        await mesh.StopNodeAsync("bob-desktop", None);
        await mesh.StartNodeAsync("bob-desktop", None);
        VerifiedRoster third = TestIdentity.RosterOf("bob", 3);
        await bob.Node.AdoptIdentityAsync(third, None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await mesh.WaitUntilAsync("alice-laptop", _ => HoldsRoster(alice, bob.User, third), "Alice queries and takes Bob's newer Roster", None);
    }

    // §7.6: Alice's laptop adopts her Roster seq 2; her phone and Bob's desktop, each established with the laptop only,
    // receive it unsolicited and hold it.
    [Fact]
    public async Task ANewRosterIsPushedToEveryEstablishedPeerWhenItsSeqRises()
    {
        await using TestMesh mesh = await Meshes.AppendixBAsync();
        await mesh.ConnectAsync("alice-laptop", "alice-phone", None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await mesh.SettleAsync(None);
        VerifiedRoster second = TestIdentity.RosterOf("alice", 2);

        await mesh["alice-laptop"].Node.AdoptIdentityAsync(second, None);

        UserId alice = mesh["alice-laptop"].User;
        await mesh.WaitUntilAsync("alice-phone", _ => HoldsRoster(mesh["alice-phone"], alice, second), "the phone holds Alice's Roster seq 2", None);
        await mesh.WaitUntilAsync("bob-desktop", _ => HoldsRoster(mesh["bob-desktop"], alice, second), "Bob holds Alice's Roster seq 2", None);
    }

    private static bool HoldsRoster(TestNode node, UserId user, VerifiedRoster roster) =>
        node.Node.Trust.TryGetUser(user, out UserTrust? held) && held.Roster?.Hash == roster.Hash;
}
