using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service.Tests.Support;
using Axiom.LDB.Storage.InMemory;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Service.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §6.7 and §9.8 through the runtime: an app's <c>ServiceCall</c> reaches the service's <c>OnCallAsync</c> with
/// the connection's session, while the service catches up, and its reply (a payload, or a service-defined status with its
/// error) comes back to the app.
/// </summary>
public sealed class CallsTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §9.8, §11.2: the payload comes back for a method the service knows; a service-defined status with its Error for one
    // it does not.
    [Fact]
    public async Task AnAppsCallReachesTheService_AndItsPayloadOrStatusComesBack()
    {
        await using TestMesh mesh = await ServiceMeshes.StartAsync();
        PackageId notes = await ServiceMeshes.NotesAsync(mesh);
        var service = new TestEchoService();
        await using RunningService echo = await mesh[ServiceMeshes.Node].RunServiceAsync(service, None);
        await ServiceMeshes.WriteAsync(mesh, notes, 2);
        await service.WaitForCountAsync(2, None);
        LocalClient app = await mesh[ServiceMeshes.Node].ConnectUserAsync("app", None);

        LocalResult<ReadOnlyMemory<byte>> echoed = await ServiceMeshes.CallAsync(app, "echo", [7, 8]);
        LocalResult<ReadOnlyMemory<byte>> counted = await ServiceMeshes.CallAsync(app, "count", []);
        LocalResult<ReadOnlyMemory<byte>> unknown = await ServiceMeshes.CallAsync(app, "nothing", []);

        Assert.Equal(new byte[] { 7, 8 }, echoed.Value.ToArray());
        Assert.Equal(2UL, BinaryPrimitives.ReadUInt64BigEndian(counted.Value.Span));
        Assert.Equal((Status)TestEchoService.UnknownMethod, unknown.Status);
        Assert.Equal("method", unknown.Error!.Detail);
    }

    // §9.8: calls are served beside the catch-up: one made while 300 Entries are still being handed over is answered.
    [Fact]
    public async Task ACallMadeWhileTheServiceCatchesUpIsAnswered()
    {
        await using TestMesh mesh = await ServiceMeshes.StartAsync();
        PackageId notes = await ServiceMeshes.NotesAsync(mesh);
        await ServiceMeshes.WriteAsync(mesh, notes, 300);
        var service = new TestEchoService();
        LocalClient app = await mesh[ServiceMeshes.Node].ConnectUserAsync("app", None);
        await using RunningService echo = await mesh[ServiceMeshes.Node].RunServiceAsync(service, None);

        LocalResult<ReadOnlyMemory<byte>> echoed = await ServiceMeshes.CallAsync(app, "echo", [1]);
        await service.WaitForCountAsync(300, None);

        Assert.True(echoed.IsOk);
        Assert.Equal(300, service.Count);
    }
    // §9.8 through the runtime: a call that arrives while the service is still connecting (its OnConnectedAsync has not
    // returned, its index not in step) waits, and is answered once the service is ready, never before.
    [Fact]
    public async Task ACallThatArrivesWhileTheServiceConnectsWaitsUntilItIsReady()
    {
        await using TestMesh mesh = await ServiceMeshes.StartAsync();
        var service = new SlowConnectingService();
        LocalClient app = await mesh[ServiceMeshes.Node].ConnectUserAsync("app", None);
        await using RunningService running = RunningService.Start(service, ServiceMeshes.EnvironmentOf(mesh, new InMemoryStorageEnvironment(), TestEchoService.Echo.Name));
        await service.Entered.WaitAsync(TestMesh.Deadline);

        Task<LocalResult<ReadOnlyMemory<byte>>> call = ServiceMeshes.CallAsync(app, "echo", [5]).AsTask();
        await Task.Yield();
        service.Release();
        LocalResult<ReadOnlyMemory<byte>> echoed = await call.WaitAsync(TestMesh.Deadline);

        Assert.True(echoed.IsOk, echoed.ToString());
        Assert.Equal((1, 0), (service.Calls, service.CallsBeforeConnected));
    }
}
