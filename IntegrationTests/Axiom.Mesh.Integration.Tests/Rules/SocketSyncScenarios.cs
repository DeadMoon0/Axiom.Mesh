using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Integration.Tests.Support;
using Axiom.Mesh.Sync;
using Axiom.Mesh.Sync.Tests.Rules;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Integration.Tests.Rules;

/// <summary>
/// The sync scenario suite (<see cref="SyncScenarios"/>, compiled from the in-memory suite's own file) on real nodes: loopback
/// sockets, real TLS 1.3, a store and a database on disk per node, the real pipe, and the sync service in process over that pipe
/// with its database in the node's directory (<see cref="RealMeshFixture"/>), on the system's clock. Scenario 6 runs at the
/// in-memory size here; its full size is a release floor (<c>SyncFloorTests</c>).
/// </summary>
public sealed class SocketSyncScenarios : SyncScenarios
{
    /// <inheritdoc />
    // Real disks and real flushes, with every suite of the solution running at once: a thousand files take minutes then.
    protected override TimeSpan StepDeadline => TimeSpan.FromSeconds(180);

    /// <inheritdoc />
    protected override async Task<IMeshFixture> StartAsync(IReadOnlyList<string> nodes) =>
        await RealMeshFixture.StartAsync(nodes, [], [], [SyncProgram.Definition], CancellationToken.None);
}
