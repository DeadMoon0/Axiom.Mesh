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
/// with its database in the node's directory (<see cref="RealMeshFixture"/>), on the system's clock. Scenario 6 runs at a
/// third of the in-memory size here, what the hosted Windows runner replicates within the settle; its full size is a release
/// floor (<c>SyncFloorTests</c>).
/// </summary>
public sealed class SocketSyncScenarios : SyncScenarios
{
    /// <inheritdoc />
    // Real disks and real flushes, with every suite of the solution running at once: a thousand files take minutes then.
    protected override TimeSpan StepDeadline => TimeSpan.FromSeconds(180);

    /// <inheritdoc />
    // The hosted Windows runner moves about 12 objects a second through two real nodes (its file system scans and flushes
    // every object written; a laptop does 1 000 files and 8 MiB in 9 s, the runner not in 180 s: STRUCTURAL-DEBT.md 88).
    // The rule here is byte equality of many files and a large one, not their number; the number is SyncFloorTests' job.
    protected override (int SmallFiles, int LargeMiB) Volume => (300, 2);

    /// <inheritdoc />
    protected override async Task<IMeshFixture> StartAsync(IReadOnlyList<string> nodes) =>
        await RealMeshFixture.StartAsync(nodes, [], [], [SyncProgram.Definition], CancellationToken.None);
}
