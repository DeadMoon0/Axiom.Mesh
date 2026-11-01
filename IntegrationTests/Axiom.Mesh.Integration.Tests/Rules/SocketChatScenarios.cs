using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Chat;
using Axiom.Mesh.Chat.Tests.Rules;
using Axiom.Mesh.Integration.Tests.Support;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Integration.Tests.Rules;

/// <summary>
/// The chat scenario suite (<see cref="ChatScenarios"/>, compiled from the in-memory suite's own file) on real nodes:
/// loopback sockets, real TLS 1.3, a store and an index on disk per node, the real pipe, and the chat service in process
/// over that pipe with its index in the node's directory (<see cref="RealMeshFixture"/>).
/// </summary>
public sealed class SocketChatScenarios : ChatScenarios
{
    /// <inheritdoc />
    protected override async Task<IMeshFixture> StartAsync(IReadOnlyList<string> nodes) =>
        await RealMeshFixture.StartAsync(nodes, [], [], [ChatProgram.Definition], CancellationToken.None);
}
