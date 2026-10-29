using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §3.4 and §4.5: a stopping node sends <c>Drain</c> with its grace; new requests are answered
/// <c>Draining</c> except <c>Unsubscribe</c>, <c>CancelOperation</c>, <c>GetOperation</c> and <c>ListOperations</c>; when
/// the grace elapsed the Host sends <c>Bye(Shutdown)</c>.
/// </summary>
public sealed class DrainTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task AfterDrainRequestsAreAnsweredDrainingExceptUnsubscribeCancelGetAndListOperations_ThenByeShutdownAtGrace()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        await using RawLocalClient client = await RawLocalClient.ReadyAsync(mesh, LocalMeshes.Node);
        uint subscribe = await client.RequestAsync(new Subscribe { SubscriptionId = 7, Topic = Topic.Store, Filter = Filter.Any });
        Assert.Equal(Status.Ok, (await client.ResponseAsync(subscribe)).Status);

        Task stopping = mesh.StopNodeAsync(LocalMeshes.Node, None);
        Drain drain = Assert.IsType<Drain>(await client.ReadAsync());
        Assert.Equal((uint)Limits.DrainGraceDefaultMilliseconds, drain.GraceMilliseconds);

        Assert.Equal(Status.Draining, await StatusOfAsync(client, new GetIdentity()));
        Assert.Equal(Status.Draining, await StatusOfAsync(client, new Subscribe { SubscriptionId = 8, Topic = Topic.Store, Filter = Filter.Any }));
        Assert.Equal(Status.Ok, await StatusOfAsync(client, new ListOperations()));
        Assert.Equal(Status.NotFound, await StatusOfAsync(client, new GetOperation { OperationId = 99 }));
        Assert.Equal(Status.NotFound, await StatusOfAsync(client, new CancelOperation { OperationId = 99 }));
        Assert.Equal(Status.Ok, await StatusOfAsync(client, new Unsubscribe { SubscriptionId = 7 }));
        Assert.False(stopping.IsCompleted);

        mesh.Clock.Advance(TimeSpan.FromMilliseconds(Limits.DrainGraceDefaultMilliseconds));
        Bye? bye = await client.EndAsync();
        Assert.Equal(ByeReason.Shutdown, bye?.Reason);
        await stopping.WaitAsync(TestMesh.Deadline);
    }

    private static async Task<Status> StatusOfAsync(RawLocalClient client, LocalRequest request) =>
        (await client.ResponseAsync(await client.RequestAsync(request))).Status;
}
