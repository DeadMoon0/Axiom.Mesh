using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Testing.Tests.Rules;

/// <summary>
/// The scenario suite (<see cref="MeshScenarios"/>) in memory: every node a full <c>MeshNode</c> on one fake clock, an
/// in-memory index and object store, connected through real TLS over in-memory streams, its local port reached over
/// in-memory pairs and its command line run in process.
/// </summary>
public sealed class InMemoryScenarios : MeshScenarios
{
    /// <inheritdoc />
    protected override async Task<IMeshFixture> StartAsync(Setup setup) =>
        await TestMesh.StartAsync(
            TestMeshOptions.Of([.. setup.Nodes]) with
            {
                WithoutIdentity = setup.WithoutIdentity,
                Severed = setup.Severed,
                Local = TestCliService.Local,
            },
            CancellationToken.None);
}
