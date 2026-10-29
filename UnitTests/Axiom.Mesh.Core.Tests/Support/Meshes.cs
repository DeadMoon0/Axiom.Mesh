using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core.Tests.Support;

/// <summary>The meshes the transport's tests start: a node alone, which then knows whichever users a test hands it.</summary>
internal static class Meshes
{
    /// <summary>A mesh of <c>alice-laptop</c> alone, knowing Bob's Roster (seq 1) besides its own.</summary>
    public static async Task<TestMesh> AliceKnowingBobAsync()
    {
        TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), CancellationToken.None);
        await mesh["alice-laptop"].Node.AcceptUserAsync(TestIdentity.RosterOf("bob"), CancellationToken.None);
        return mesh;
    }

    /// <summary>Alice's laptop and Bob's desktop, each knowing the other's user.</summary>
    public static Task<TestMesh> AliceAndBobAsync() =>
        TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop", "bob-desktop") with { Trust = TrustSeed.MutuallyKnown("alice-laptop", "bob-desktop") }, CancellationToken.None);

    /// <summary>
    /// Alice's laptop and phone and Bob's desktop, every one knowing every user, with the phone and the desktop severed:
    /// the laptop is between them, as in SPEC.md Appendix B.
    /// </summary>
    public static Task<TestMesh> AppendixBAsync() => TestMesh.StartAsync(
        TestMeshOptions.Of("alice-laptop", "alice-phone", "bob-desktop") with
        {
            Trust = TrustSeed.MutuallyKnown("alice-laptop", "alice-phone", "bob-desktop"),
            Severed = [("alice-phone", "bob-desktop")],
        },
        CancellationToken.None);
}
