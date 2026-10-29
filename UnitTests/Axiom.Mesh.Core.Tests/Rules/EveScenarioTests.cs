using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// The Eve scenario (plan §5; SPEC.md §3, §4.3): Alice and Bob know each other, nobody knows Eve. Eve's device is never
/// dialled, is closed without a byte when it connects, and never becomes established, even once Eve learns Alice's key.
/// </summary>
public sealed class EveScenarioTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task EveIsClosedWithoutAByte_NeverDialled_AndNeverEstablished()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(
            TestMeshOptions.Of("alice-laptop", "bob-desktop", "eve-device") with { Trust = TrustSeed.MutuallyKnown("alice-laptop", "bob-desktop") },
            None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        TestNode eve = mesh["eve-device"];

        // Eve connects by hand: the handshake refuses her key.
        await using (ScriptedPeer scripted = await ScriptedPeer.ConnectAsync(mesh["alice-laptop"], eve.Identity, ScriptedPeerOptions.Default, None))
            await scripted.ExpectClosedWithoutHelloAsync(None);

        // Eve learns Alice's key (a Roster leaked to her) and dials her: Alice refuses at the handshake, Eve backs off.
        await eve.Node.AcceptUserAsync(TestIdentity.RosterOf("alice"), None);
        await mesh.WaitForFactAsync("eve-device", f => f.Kind == NodeFactKind.BackedOff && f.Device == mesh["alice-laptop"].Device, 0, "Eve refused by Alice", None);

        foreach (string name in new[] { "alice-laptop", "bob-desktop" })
        {
            MeshNode node = mesh[name].Node;
            Assert.DoesNotContain(node.Peers, p => p.Device == eve.Device);
            Assert.DoesNotContain(node.Facts.Recent(), f => f.Device == eve.Device && f.Kind is NodeFactKind.Dialling or NodeFactKind.SessionOpened or NodeFactKind.Established);
        }

        Assert.DoesNotContain(eve.Node.Peers, p => p.State == PeerState.Established);
        Assert.DoesNotContain(eve.Node.Facts.Recent(), f => f.Kind == NodeFactKind.Established);
    }
}
