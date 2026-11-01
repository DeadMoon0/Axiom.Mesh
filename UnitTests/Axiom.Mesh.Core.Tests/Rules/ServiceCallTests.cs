using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §6.7 and §9.8, <c>ServiceCall</c> end to end on a node's local port: forwarded verbatim as a
/// <c>ForwardedCall</c>, the reply passed back with its status and <c>Error</c>; <c>ServiceUnavailable</c> at once for a
/// service that is not there and when its connection ends mid-call; <c>ServiceTimeout</c> at 60 seconds with the late reply
/// changing nothing; <c>Busy</c> for the 65th outstanding call; a slow call beside the caller's other requests; a reply that
/// is not the callback's layout a violation of the service's connection.
/// </summary>
public sealed class ServiceCallTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(Limits.ServiceCallSeconds);

    // §6.7, §9.8, §11.2: the service sees the caller's number and name, the method and the payload as sent; its payload
    // comes back as the OK body, its failure with the very status and Error it chose, a service-defined code included.
    [Fact]
    public async Task ACallIsForwardedVerbatim_AndTheServicesPayloadOrStatusAndErrorComeBackUnchanged()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        var echo = new Echo();
        await node.ConnectAsync(new LocalClientOptions { Role = Role.Service, Name = "cli", Token = LocalMeshes.CliToken, Callbacks = echo }, TestProcess.Served, None);
        LocalClient app = await node.ConnectUserAsync("chat-app", None);

        LocalResult<ReadOnlyMemory<byte>> echoed = await app.SendAsync(new ServiceCall { Service = "cli", Method = "echo", Payload = new byte[] { 1, 2, 3 } }, None);
        LocalResult<ReadOnlyMemory<byte>> failed = await app.SendAsync(new ServiceCall { Service = "cli", Method = "fail", Payload = ReadOnlyMemory<byte>.Empty }, None);

        Assert.True(echoed.IsOk);
        Assert.Equal(new byte[] { 1, 2, 3 }, echoed.Value.ToArray());
        ForwardedCall seen = echo.Calls.First(c => c.Method == "echo");
        Assert.Equal("chat-app", seen.ClientName);
        Assert.Equal(new byte[] { 1, 2, 3 }, seen.Payload.ToArray());
        Assert.NotEqual(0u, seen.Client);
        Assert.Equal((Status)32_770, failed.Status);
        Assert.Equal(("not yours", "message 7"), (failed.Error!.Message, failed.Error.Detail));
    }

    // §6.7: a service the configuration does not list, and a configured one that is not connected, are ServiceUnavailable
    // at once: nothing waits for the 60 seconds.
    [Fact]
    public async Task AServiceThatIsNotConfiguredOrNotConnectedIsUnavailableAtOnce()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        LocalClient app = await mesh[LocalMeshes.Node].ConnectUserAsync("app", None);

        LocalResult<ReadOnlyMemory<byte>> unknown = await app.SendAsync(new ServiceCall { Service = "nobody", Method = "about" }, None);
        LocalResult<ReadOnlyMemory<byte>> absent = await app.SendAsync(new ServiceCall { Service = "worker", Method = "about" }, None);

        Assert.Equal((Status.ServiceUnavailable, Status.ServiceUnavailable), (unknown.Status, absent.Status));
        Assert.Contains("not configured", unknown.Error!.Message, StringComparison.Ordinal);
        Assert.Contains("not connected", absent.Error!.Message, StringComparison.Ordinal);
    }

    // §9.8, §3.6: no reply within 60 seconds is ServiceTimeout for the caller; the service's late reply is ignored and the
    // service's connection carries on.
    [Fact]
    public async Task AServiceThatDoesNotReplyWithin60SecondsTimesOut_AndItsLateReplyChangesNothing()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        await using RawLocalClient service = await ServiceAsync(mesh);
        LocalClient app = await mesh[LocalMeshes.Node].ConnectUserAsync("app", None);

        ValueTask<LocalResult<ReadOnlyMemory<byte>>> call = app.SendAsync(new ServiceCall { Service = "cli", Method = "slow" }, None);
        Callback callback = Assert.IsType<Callback>(await service.ReadAsync());
        mesh.Clock.Advance(Deadline);
        LocalResult<ReadOnlyMemory<byte>> result = await call.AsTask().WaitAsync(TestMesh.Deadline);
        await service.SendAsync(Reply.Ok<ForwardedCall, ReadOnlyMemory<byte>>(callback.CallbackId, new byte[] { 9 }));

        Assert.Equal(Status.ServiceTimeout, result.Status);
        uint identity = await service.RequestAsync(new GetIdentity());
        Assert.Equal(Status.Ok, (await service.ResponseAsync(identity)).Status);
        Assert.True((await app.SendAsync(new GetIdentity(), None)).IsOk);
    }

    // §9.8, §12: with 64 calls outstanding toward a service, the 65th is Busy at once, whoever sends it.
    [Fact]
    public async Task The65thOutstandingCallTowardAServiceIsBusy()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        await using RawLocalClient service = await ServiceAsync(mesh);
        TestNode node = mesh[LocalMeshes.Node];
        LocalClient first = await node.ConnectUserAsync("first", None);
        LocalClient second = await node.ConnectUserAsync("second", None);

        Task<LocalResult<ReadOnlyMemory<byte>>>[] outstanding = [.. Enumerable.Range(0, Limits.MaxOutstandingCallbacks)
            .Select(i => first.SendAsync(new ServiceCall { Service = "cli", Method = "m" + i }, None).AsTask())];
        for (int i = 0; i < Limits.MaxOutstandingCallbacks; i++)
            Assert.IsType<Callback>(await service.ReadAsync());
        LocalResult<ReadOnlyMemory<byte>> refused = await second.SendAsync(new ServiceCall { Service = "cli", Method = "one-more" }, None);

        Assert.Equal(Status.Busy, refused.Status);
        Assert.All(outstanding, t => Assert.False(t.IsCompleted));
    }

    // §6.7, §2.3: a service whose connection ends before it replied leaves its caller with ServiceUnavailable.
    [Fact]
    public async Task AServiceWhoseConnectionEndsMidCallLeavesItsCallerUnavailable()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        RawLocalClient service = await ServiceAsync(mesh);
        LocalClient app = await mesh[LocalMeshes.Node].ConnectUserAsync("app", None);

        ValueTask<LocalResult<ReadOnlyMemory<byte>>> call = app.SendAsync(new ServiceCall { Service = "cli", Method = "work" }, None);
        Assert.IsType<Callback>(await service.ReadAsync());
        await service.DisposeAsync();

        Assert.Equal(Status.ServiceUnavailable, (await call.AsTask().WaitAsync(TestMesh.Deadline)).Status);
    }

    // §3.4, §6.7: a call waiting on a slow service holds up none of the caller's other requests, ordered ones included.
    [Fact]
    public async Task ACallWaitingOnASlowServiceDoesNotHoldUpTheCallersOtherRequests()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        await using RawLocalClient service = await ServiceAsync(mesh);
        LocalClient app = await mesh[LocalMeshes.Node].ConnectUserAsync("app", None);

        ValueTask<LocalResult<ReadOnlyMemory<byte>>> call = app.SendAsync(new ServiceCall { Service = "cli", Method = "slow" }, None);
        Callback callback = Assert.IsType<Callback>(await service.ReadAsync());
        LocalResult<Subscription<ServiceEvent>> subscribed = await app.SubscribeAsync<ServiceEvent>(Filter.Any, None);
        LocalResult<Identity> identity = await app.SendAsync(new GetIdentity(), None);

        Assert.True(subscribed.IsOk && identity.IsOk);
        Assert.False(call.IsCompleted);
        await service.SendAsync(Reply.Ok<ForwardedCall, ReadOnlyMemory<byte>>(callback.CallbackId, ReadOnlyMemory<byte>.Empty));
        Assert.True((await call.AsTask().WaitAsync(TestMesh.Deadline)).IsOk);
    }

    // §9.8, §11.3: a Reply whose OK body is not one payload is a violation of the service's connection, closed with
    // Bye(Violation); its caller hears ServiceUnavailable.
    [Fact]
    public async Task AMalformedReplyClosesTheServicesConnectionAsAViolation_AndItsCallerHearsUnavailable()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        await using RawLocalClient service = await ServiceAsync(mesh);
        LocalClient app = await mesh[LocalMeshes.Node].ConnectUserAsync("app", None);

        ValueTask<LocalResult<ReadOnlyMemory<byte>>> call = app.SendAsync(new ServiceCall { Service = "cli", Method = "broken" }, None);
        Callback callback = Assert.IsType<Callback>(await service.ReadAsync());
        await service.WriteRawAsync(MalformedOkReply(callback.CallbackId));

        Assert.Equal(Status.ServiceUnavailable, (await call.AsTask().WaitAsync(TestMesh.Deadline)).Status);
        Assert.Equal(ByeReason.Violation, (await service.EndAsync())?.Reason);
    }

    // A Reply frame (33) with status Ok whose body is one byte: no blob (a u32 length first) fits in it.
    private static byte[] MalformedOkReply(uint callbackId)
    {
        byte[] frame = new byte[4 + 2 + 4 + 2 + 1];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)(frame.Length - 4));
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), 33);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(6), callbackId);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(10), 0);
        frame[12] = 0xFF;
        return frame;
    }

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

    // Echoes "echo", fails "fail" with a service-defined status; records every call it saw.
    private sealed class Echo : CallbackHandler
    {
        public ConcurrentQueue<ForwardedCall> Calls { get; } = new();

        protected override ValueTask<CallReply> OnForwardedCallAsync(ForwardedCall call, CancellationToken deadline)
        {
            Calls.Enqueue(call);
            return ValueTask.FromResult(call.Method == "echo"
                ? CallReply.Ok(call.Payload)
                : CallReply.Failure((Status)32_770, new Error { Message = "not yours", Detail = "message 7" }));
        }
    }
}
