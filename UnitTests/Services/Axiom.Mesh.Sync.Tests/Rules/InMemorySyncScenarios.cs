using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// The sync scenario suite (<see cref="SyncScenarios"/>) in memory: every node a full <c>MeshNode</c> on one fake clock, its local
/// port listing the sync service, which runs in process beside it over an in-memory pair with an in-memory database; the bound
/// directories are real directories on disk.
/// </summary>
public sealed class InMemorySyncScenarios : SyncScenarios
{
    /// <inheritdoc />
    protected override async Task<IMeshFixture> StartAsync(IReadOnlyList<string> nodes) =>
        await TestMesh.StartAsync(
            TestMeshOptions.Of([.. nodes]) with { Local = TestServices.With(TestCliService.Local, SyncProgram.Definition) },
            CancellationToken.None);
}
