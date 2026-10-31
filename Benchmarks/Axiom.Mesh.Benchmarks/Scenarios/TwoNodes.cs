using System.Threading.Tasks;
using Axiom.Mesh.Integration.Tests.Support;
using Axiom.Mesh.Local;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Benchmarks.Scenarios;

/// <summary>Alice's laptop and Bob's desktop on loopback, paired and established, and a Package of Alice's granted to Bob.</summary>
internal static class TwoNodes
{
    public const string Alice = "alice-laptop";
    public const string Bob = "bob-desktop";

    /// <summary>The two nodes, paired and established.</summary>
    /// <returns>The mesh.</returns>
    public static async Task<RealMeshFixture> StartAsync()
    {
        RealMeshFixture mesh = await RealMeshFixture.StartAsync([Alice, Bob], [], [], ScenarioRunner.None).ConfigureAwait(false);
        await mesh.PairAsync(Alice, Bob, ScenarioRunner.None).ConfigureAwait(false);
        await mesh.ConnectAsync(Alice, Bob, ScenarioRunner.None).ConfigureAwait(false);
        return mesh;
    }

    /// <summary>A Package of Alice's, Bob granted Read.</summary>
    /// <param name="mesh">The mesh.</param>
    /// <param name="name">The Package's name.</param>
    /// <param name="kind">Its kind.</param>
    /// <returns>Its id.</returns>
    public static async Task<PackageId> CreateAsync(RealMeshFixture mesh, string name, PackageKind kind)
    {
        await mesh.HoldUserKeyAsync(Alice, ScenarioRunner.None).ConfigureAwait(false);
        await using LocalClient alice = await mesh.ConnectUserAsync(Alice, "benchmark", ScenarioRunner.None).ConfigureAwait(false);
        PackageId package = (await alice.SendAsync(new CreatePackage { Kind = kind, Name = name }, ScenarioRunner.None).ConfigureAwait(false)).Value.PackageId;
        await alice.SendAsync(new SetGrant { PackageId = package, GranteeId = TestIdentity.Of(Bob).UserId, Rights = GrantRights.Read }, ScenarioRunner.None).ConfigureAwait(false);
        return package;
    }

    /// <summary>Bob follows the Package.</summary>
    /// <param name="mesh">The mesh.</param>
    /// <param name="package">The Package.</param>
    /// <returns>A task.</returns>
    public static async Task FollowAsync(RealMeshFixture mesh, PackageId package)
    {
        await using LocalClient bob = await mesh.ConnectUserAsync(Bob, "benchmark", ScenarioRunner.None).ConfigureAwait(false);
        await bob.SendAsync(new FollowPackage { PackageId = package }, ScenarioRunner.None).ConfigureAwait(false);
    }
}
