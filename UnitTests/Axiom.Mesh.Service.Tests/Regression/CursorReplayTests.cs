using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Storage.InMemory;
using Axiom.Mesh.Service.Tests.Support;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Service.Tests.Regression;

/// <summary>
/// The order of the runtime's two commits per Entry (<c>ServiceRunner</c>, plan §9.2 item 4): the service's handler first,
/// the cursor after, in a transaction of its own. A handler that fails leaves the cursor where it was.
/// </summary>
public sealed class CursorReplayTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // Damage: the cursor was advanced before the handler ran, so an Entry whose handler failed (or whose process died in
    // between) was never handed over again and was missing from the service's index for good.
    [Fact]
    public async Task AnEntryWhoseHandlerFailedIsHandedOverAgainOnTheNextRun()
    {
        await using TestMesh mesh = await ServiceMeshes.StartAsync();
        PackageId notes = await ServiceMeshes.NotesAsync(mesh);
        var written = await ServiceMeshes.WriteAsync(mesh, notes, 3);
        var storage = new InMemoryStorageEnvironment();
        var failing = new RecordingService();
        failing.FailOnceAt(2);

        RunningService first = RunningService.Start(failing, ServiceMeshes.EnvironmentOf(mesh, storage, "echo"));
        ServiceExitCode exit = await first.Exited.WaitAsync(TestMesh.Deadline);
        await first.DisposeAsync();
        var second = new TestEchoService();
        await using RunningService again = await ServiceMeshes.ConnectedAsync(RunningService.Start(second, ServiceMeshes.EnvironmentOf(mesh, storage, "echo")));
        await second.WaitForCountAsync(2, None);

        Assert.Equal(ServiceExitCode.State, exit);
        Assert.Equal([written[0]], failing.Echo.Seen.Select(e => e.Hash));
        Assert.Equal([written[1], written[2]], second.Seen.Select(e => e.Hash));
    }
}
