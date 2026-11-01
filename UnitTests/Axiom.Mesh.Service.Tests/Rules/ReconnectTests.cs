using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Storage.InMemory;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service.Tests.Support;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Service.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §2.3 and §4.2 (decision 27), what a service does when its connection fails: it reconnects with the token
/// it has, 1 second after the first failure and doubling up to 30; a Host that refuses its name or its token, or speaks
/// another version, makes it exit with <see cref="ServiceExitCode.Refused"/>.
/// </summary>
public sealed class ReconnectTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Theory]
    [InlineData(1, 1)] // 1 s after the first failure
    [InlineData(2, 2)] // doubling
    [InlineData(5, 16)] // still doubling
    [InlineData(6, 30)] // capped at 30 s
    [InlineData(40, 30)] // and capped for good
    public void ALostOrUnreachableHostIsRetriedAfterTheBackoffOfSection2Point3(int attempt, int seconds)
    {
        ReconnectDecision decision = ReconnectRule.Decide(null, string.Empty, attempt);

        Assert.Equal((ReconnectAction.Retry, TimeSpan.FromSeconds(seconds)), (decision.Action, decision.Delay));
    }

    [Theory]
    [InlineData(ByeReason.Refused, "service", ReconnectAction.Exit)] // the name or its token is refused: retrying cannot help
    [InlineData(ByeReason.Refused, "version", ReconnectAction.Exit)] // another protocol version
    [InlineData(ByeReason.Refused, "busy", ReconnectAction.Retry)] // a limit, for now
    [InlineData(ByeReason.Duplicate, "", ReconnectAction.Retry)] // the Host has not yet seen the old connection end
    [InlineData(ByeReason.Shutdown, "", ReconnectAction.Retry)] // the Host was stopping
    public void OnlyARefusedNameTokenOrVersionMakesTheServiceExit(ByeReason reason, string detail, ReconnectAction action)
    {
        ReconnectDecision decision = ReconnectRule.Decide(reason, detail, 1);

        Assert.Equal(action, decision.Action);
        Assert.Equal(action == ReconnectAction.Exit ? ServiceExitCode.Refused : ServiceExitCode.Ok, decision.ExitCode);
    }

    // §2.3: the Host stops and starts again; the service, which was run by hand, reconnects on its clock after the backoff,
    // with the same token, and receives what was stored meanwhile.
    [Fact]
    public async Task AServiceWhoseHostRestartedReconnectsAfterTheBackoff_AndCatchesUp()
    {
        await using TestMesh mesh = await ServiceMeshes.StartAsync();
        PackageId notes = await ServiceMeshes.NotesAsync(mesh);
        var service = new RecordingService();
        await using RunningService echo = await mesh[ServiceMeshes.Node].RunServiceAsync(service, None);
        await ServiceMeshes.WriteAsync(mesh, notes, 1);
        await service.Echo.WaitForCountAsync(1, None);

        await mesh.StopNodeAsync(ServiceMeshes.Node, None);
        await mesh.StartNodeAsync(ServiceMeshes.Node, None);
        await ServiceMeshes.WriteAsync(mesh, notes, 1);

        // The runtime waits for its backoff on the mesh's clock: move it a second at a time until the Entry arrived.
        for (int second = 0; service.Echo.Count < 2; second++)
        {
            Assert.True(second < 120, "the service did not reconnect");
            mesh.Clock.Advance(TimeSpan.FromSeconds(1));
            using var glance = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            try
            {
                await service.Echo.WaitForCountAsync(2, glance.Token);
            }
            catch (OperationCanceledException)
            {
                // Not yet: move the clock on.
            }
        }

        Assert.Equal(2, service.Plans.Count);
        Assert.False(echo.Exited.IsCompleted);
        Assert.True(service.Disconnects >= 1);
    }

    // §4.2 check 3: a service whose name the Host does not list is refused for good: the runtime exits Refused instead of
    // retrying in a loop.
    [Fact]
    public async Task AServiceTheHostRefusesExitsRefused()
    {
        await using TestMesh mesh = await ServiceMeshes.StartAsync();
        var stranger = new TestEchoService(TestEchoService.Echo with { Name = "stranger" });

        RunningService running = RunningService.Start(stranger, ServiceMeshes.EnvironmentOf(mesh, new InMemoryStorageEnvironment(), "stranger"));
        ServiceExitCode exit = await running.Exited.WaitAsync(TestMesh.Deadline);
        await running.DisposeAsync();

        Assert.Equal(ServiceExitCode.Refused, exit);
        Assert.Empty(stranger.Seen);
    }
}
