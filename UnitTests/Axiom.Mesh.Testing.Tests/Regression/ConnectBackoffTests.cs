using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;

namespace Axiom.Mesh.Testing.Tests.Regression;

/// <summary>
/// <see cref="TestMesh.ConnectAsync"/> when both nodes hold a backoff for each other: right after a pairing both may dial at
/// once and both be refused (one dialled before the other had stored the Roster), and neither dials again until its
/// backoff ends on the mesh's fake clock.
/// </summary>
public sealed class ConnectBackoffTests
{
    private const string Alice = "alice-laptop";
    private const string Bob = "bob-desktop";
    private static readonly CancellationToken None = CancellationToken.None;

    // Damage: ConnectAsync waited for a fact that only the passing of the fake clock could bring, and timed out on the real
    // clock (an InMemoryChatScenarios flake under full parallel load).
    [Fact]
    public async Task ConnectAsyncMovesTheClockPastABackoffBothNodesHoldForEachOther()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of(Alice, Bob) with { Trust = TrustSeed.MutuallyKnown(Alice, Bob) }, None);
        await mesh.ConnectAsync(Alice, Bob, None);

        mesh.Sever(Alice, Bob);
        await mesh.WaitUntilAsync(Alice, n => n.Peers.Any(p => p.Device == mesh[Bob].Device && p.Sessions == 0), "the session to Bob ended", None);
        await mesh.WaitUntilAsync(Bob, n => n.Peers.Any(p => p.Device == mesh[Alice].Device && p.Sessions == 0), "the session to Alice ended", None);
        await DialAsync(mesh, Alice, Bob);
        await DialAsync(mesh, Bob, Alice);
        await mesh.WaitUntilAsync(Alice, n => BackedOff(n, mesh[Bob].Device, mesh), "a backoff toward Bob", None);
        await mesh.WaitUntilAsync(Bob, n => BackedOff(n, mesh[Alice].Device, mesh), "a backoff toward Alice", None);
        mesh.Heal();
        await mesh.ConnectAsync(Alice, Bob, None);

        Assert.True(MeshWaits.IsEstablishedWith(mesh[Alice].Node, mesh[Bob].Device));
    }

    // A dial while the link is cut fails, and the dialling node backs off (SPEC.md §4.4).
    private static async Task DialAsync(TestMesh mesh, string from, string to)
    {
        await using Local.LocalClient client = await mesh[from].ConnectUserAsync("backoff-test", None);
        Local.LocalResult<Local.NoContent> dialled = await client.SendAsync(new Local.Dial { DeviceId = mesh[to].Device, Address = mesh[to].Address.ToString() }, None);
        Assert.True(dialled.IsOk, dialled.ToString());
    }

    private static bool BackedOff(MeshNode node, DeviceId peer, TestMesh mesh) =>
        node.Peers.FirstOrDefault(p => p.Device == peer) is { Sessions: 0, BackoffUntil: { } until } && until > mesh.Clock.GetUtcNow();
}
