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

    /// <summary>
    /// Alice's laptop and Bob's desktop, each knowing the other's user, both silent: nothing connects until the test
    /// calls <see cref="TestMesh.ConnectAsync"/>, so whatever the test sets up first (a Roster, a subscription, a follow)
    /// is in place before the first <c>Hello</c>.
    /// </summary>
    public static Task<TestMesh> AliceAndBobAsync() => TestMesh.StartAsync(
        TestMeshOptions.Of("alice-laptop", "bob-desktop") with
        {
            Trust = TrustSeed.MutuallyKnown("alice-laptop", "bob-desktop"),
            Silent = ["alice-laptop", "bob-desktop"],
        },
        CancellationToken.None);

    /// <summary>
    /// Alice's laptop and phone and Bob's desktop, every one knowing every user, with the phone and the desktop severed:
    /// the laptop is between them, as in SPEC.md Appendix B. All three are silent: nothing connects until the test calls
    /// <see cref="TestMesh.ConnectAsync"/>.
    /// </summary>
    public static Task<TestMesh> AppendixBAsync() => TestMesh.StartAsync(
        TestMeshOptions.Of("alice-laptop", "alice-phone", "bob-desktop") with
        {
            Trust = TrustSeed.MutuallyKnown("alice-laptop", "alice-phone", "bob-desktop"),
            Severed = [("alice-phone", "bob-desktop")],
            Silent = ["alice-laptop", "alice-phone", "bob-desktop"],
        },
        CancellationToken.None);
}
