using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Chat.Tests.Rules;

/// <summary>
/// The chat scenario suite (<see cref="ChatScenarios"/>) in memory: every node a full <c>MeshNode</c> on one fake clock, its
/// local port listing the chat service, which runs in process beside it over an in-memory pair with an in-memory index.
/// </summary>
public sealed class InMemoryChatScenarios : ChatScenarios
{
    /// <inheritdoc />
    protected override async Task<IMeshFixture> StartAsync(IReadOnlyList<string> nodes) =>
        await TestMesh.StartAsync(
            TestMeshOptions.Of([.. nodes]) with { Local = TestServices.With(TestCliService.Local, ChatProgram.Definition) },
            CancellationToken.None);
}
