using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §3.6 and §9.8, callbacks to a service: a <c>Reply</c> completes its callback; at the deadline the callback
/// leaves the outstanding set and its caller hears nothing more; a late <c>Reply</c> within 5 minutes is ignored; a
/// <c>Reply</c> for an id the Host never issued is a violation; at most 64 are outstanding.
/// </summary>
public sealed class CallbackTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    // §3.6: the service's Reply carries the callback's id and completes it with its body.
    [Fact]
    public async Task AReplyCompletesItsCallback_ALateOneIsIgnored_OneNeverIssuedIsAViolation()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        await using RawLocalClient service = await ServiceAsync(mesh);
        LocalConnection connection = mesh[LocalMeshes.Node].Node.Local.Book.Live().Single(c => c.Role == Role.Service);

        Task<CallbackOutcome> answered = connection.CallAsync(Call("ping"), Deadline, out bool busy);
        Callback first = Assert.IsType<Callback>(await service.ReadAsync());
        await service.SendAsync(Reply.Ok<ForwardedCall, ReadOnlyMemory<byte>>(first.CallbackId, new byte[] { 7 }));
        CallbackOutcome outcome = await answered.WaitAsync(TestMesh.Deadline);

        Assert.False(busy);
        Assert.Equal(CallbackEnd.Replied, outcome.End);
        Assert.True(outcome.Reply!.TryReadOk<ForwardedCall, ReadOnlyMemory<byte>>(out ReadOnlyMemory<byte> body));
        Assert.Equal(new byte[] { 7 }, body.ToArray());

        Task<CallbackOutcome> expiring = connection.CallAsync(Call("slow"), Deadline, out _);
        Callback second = Assert.IsType<Callback>(await service.ReadAsync());
        mesh.Clock.Advance(Deadline);
        Assert.Equal(CallbackEnd.Expired, (await expiring.WaitAsync(TestMesh.Deadline)).End);
        await service.SendAsync(Reply.Ok<ForwardedCall, ReadOnlyMemory<byte>>(second.CallbackId, ReadOnlyMemory<byte>.Empty));
        uint correlation = await service.RequestAsync(new GetIdentity());
        Assert.Equal(Status.Ok, (await service.ResponseAsync(correlation)).Status);

        await service.SendAsync(Reply.Ok<ForwardedCall, ReadOnlyMemory<byte>>(second.CallbackId + 100, ReadOnlyMemory<byte>.Empty));
        Assert.Equal(ByeReason.Violation, (await service.EndAsync())?.Reason);
    }

    // §9.8, §12: with 64 callbacks outstanding toward a service, the 65th is refused (Busy for the caller); the
    // connection's end completes every outstanding one without a reply.
    [Fact]
    public async Task A65thOutstandingCallbackIsRefused_AndTheConnectionsEndCompletesTheRest()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        await using RawLocalClient service = await ServiceAsync(mesh);
        LocalConnection connection = mesh[LocalMeshes.Node].Node.Local.Book.Live().Single(c => c.Role == Role.Service);

        Task<CallbackOutcome>[] outstanding = [.. Enumerable.Range(0, Limits.MaxOutstandingCallbacks).Select(i => connection.CallAsync(Call($"call{i}"), Deadline, out _))];
        _ = connection.CallAsync(Call("one-too-many"), Deadline, out bool busy);

        Assert.True(busy);
        Assert.Equal(Limits.MaxOutstandingCallbacks, connection.Callbacks.Outstanding);
        await service.DisposeAsync();
        CallbackOutcome[] ended = await Task.WhenAll(outstanding).WaitAsync(TestMesh.Deadline);
        Assert.All(ended, e => Assert.Equal(CallbackEnd.ConnectionEnded, e.End));
    }

    private static ForwardedCall Call(string method) => new() { Client = 1, ClientName = "app", Method = method };

    private static async Task<RawLocalClient> ServiceAsync(TestMesh mesh)
    {
        (RawLocalClient client, IHostMessage? answer) = await RawLocalClient.HelloAsync(
            mesh,
            LocalMeshes.Node,
            new Hello { Role = Role.Service, Name = "cli", Token = LocalMeshes.CliToken },
            TestProcess.Served);
        Assert.IsType<HelloAck>(answer);
        return client;
    }
}
