using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Storage.InMemory;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service.Tests.Support;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Service.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §9.3 and §7.2, catching up by store sequence: while more than 256 Entries are missing the runtime pages with
/// <c>ListStored</c>, then subscribes from where it stands; a service that comes back resumes after its cursor without
/// repeats; under another store epoch its index is discarded and it starts from store sequence 1.
/// </summary>
public sealed class CatchUpTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Theory]
    [InlineData(1UL, 0UL, CatchUpAction.Subscribe, 1UL)] // an empty store: subscribe from 1
    [InlineData(1UL, 256UL, CatchUpAction.Subscribe, 1UL)] // exactly 256 missing: one replay carries them
    [InlineData(1UL, 257UL, CatchUpAction.Page, 1UL)] // 257 missing: page first
    [InlineData(0UL, 10UL, CatchUpAction.Subscribe, 1UL)] // 0 reads as 1
    [InlineData(745UL, 1000UL, CatchUpAction.Subscribe, 745UL)] // the rest fits a replay
    [InlineData(1001UL, 1000UL, CatchUpAction.Subscribe, 1001UL)] // caught up: subscribe live from the next
    public void PageWhileMoreThan256AreMissing_ThenSubscribeFromWhereItStands(ulong fromSeq, ulong hostSeq, CatchUpAction action, ulong from)
    {
        CatchUpStep step = CatchUpStep.Next(fromSeq, hostSeq);

        Assert.Equal((action, from), (step.Action, step.FromSeq));
    }

    // §7.2, §9.3: 300 Entries stored while the service was away arrive once each, in store order: paged, then replayed.
    [Fact]
    public async Task AServiceAwayWhile300EntriesWereStoredReceivesEachOnceInOrder()
    {
        await using TestMesh mesh = await ServiceMeshes.StartAsync();
        PackageId notes = await ServiceMeshes.NotesAsync(mesh);
        var written = await ServiceMeshes.WriteAsync(mesh, notes, 300);
        var service = new TestEchoService();

        await using RunningService echo = await mesh[ServiceMeshes.Node].RunServiceAsync(service, None);
        await service.WaitForCountAsync(300, None);

        Assert.Equal(written, service.Seen.Select(e => e.Hash));
    }

    // §9.3: a service started again on its state resumes one past its cursor: it receives what was stored meanwhile, and
    // nothing it handled before.
    [Fact]
    public async Task AServiceStartedAgainResumesAfterItsCursor_WithoutRepeats()
    {
        await using TestMesh mesh = await ServiceMeshes.StartAsync();
        PackageId notes = await ServiceMeshes.NotesAsync(mesh);
        var first = new TestEchoService();
        await using (RunningService running = await mesh[ServiceMeshes.Node].RunServiceAsync(first, None))
        {
            await ServiceMeshes.WriteAsync(mesh, notes, 2);
            await first.WaitForCountAsync(2, None);
        }

        var meanwhile = await ServiceMeshes.WriteAsync(mesh, notes, 3);
        var second = new TestEchoService();
        await using RunningService again = await mesh[ServiceMeshes.Node].RunServiceAsync(second, None);
        await second.WaitForCountAsync(3, None);
        var later = await ServiceMeshes.WriteAsync(mesh, notes, 1);
        await second.WaitForCountAsync(4, None);

        Assert.Equal([.. meanwhile, .. later], second.Seen.Select(e => e.Hash));
    }

    // §9.3: an index built under another store epoch is discarded (the service is told) and rebuilt from store sequence 1.
    [Fact]
    public async Task AnIndexOfAnotherStoreEpochIsDiscardedAndRebuiltFromTheStart()
    {
        await using TestMesh mesh = await ServiceMeshes.StartAsync();
        PackageId notes = await ServiceMeshes.NotesAsync(mesh);
        var written = await ServiceMeshes.WriteAsync(mesh, notes, 2);
        var storage = new InMemoryStorageEnvironment();
        await using (Database old = await Database.OpenAsync(storage, DatabaseOptions.Default, None))
            await (await ServiceState.OpenAsync(old, None)).AdvanceAsync(Hash32.Of([42]), 9, None);
        var service = new RecordingService();

        await using RunningService running = await ServiceMeshes.ConnectedAsync(RunningService.Start(service, ServiceMeshes.EnvironmentOf(mesh, storage, "echo")));
        await service.Echo.WaitForCountAsync(2, None);

        IndexCatchUp plan = Assert.Single(service.Plans);
        Assert.Equal((true, 1UL), (plan.DiscardIndex, plan.FromSeq));
        Assert.Equal(written, service.Echo.Seen.Select(e => e.Hash));
    }
}
