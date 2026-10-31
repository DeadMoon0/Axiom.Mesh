using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Integration.Tests.Support;
using Axiom.Mesh.Testing;
using Axiom.Mesh.Testing.Tests.Rules;

namespace Axiom.Mesh.Integration.Tests.Rules;

/// <summary>
/// The scenario suite (<see cref="MeshScenarios"/>, compiled from the in-memory suite's own file) on real nodes: loopback
/// sockets, real TLS 1.3, a store and an index on disk per node, the real pipe and the real command line over it
/// (<see cref="RealMeshFixture"/>). The same scenario passes in memory and on sockets.
/// </summary>
public sealed class SocketScenarios : MeshScenarios
{
    /// <inheritdoc />
    protected override async Task<IMeshFixture> StartAsync(Setup setup) =>
        await RealMeshFixture.StartAsync(setup.Nodes, setup.WithoutIdentity, setup.Severed, CancellationToken.None);
}
