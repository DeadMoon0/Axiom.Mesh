using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Axiom.Mesh.Local.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §3.6 and §9.8 from the service's side: the client hands each <c>ForwardedCall</c> to the service's handler
/// off its read loop and sends the one <c>Reply</c> the handler returns; a handler that fails answers <c>IoError</c> with
/// the fault's type; one stopped at the 60-second deadline answers nothing; no reply leaves later than 120 seconds after
/// its callback arrived; a client without a handler answers <c>UnknownRequest</c>.
/// </summary>
public sealed class CallbackServingTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    // §9.8: the handler's payload is the Reply's OK body; its failure is the Reply's status and Error, a service-defined
    // code included.
    [Fact]
    public async Task AForwardedCallIsAnsweredWithWhatTheHandlerReturned_PayloadOrStatusAndError()
    {
        var handler = new Handler(call => Task.FromResult(call.Method == "echo"
            ? CallReply.Ok(call.Payload)
            : CallReply.Failure((Status)40_000, new Error { Message = "no such channel", Detail = "channel" })));
        (LocalClient client, ScriptedHost host) = await ConnectAsync(handler, TimeProvider.System);
        await using (host)
        await using (client)
        {
            await host.SendAsync(new Callback(7, Call("echo", [1, 2])));
            await host.SendAsync(new Callback(8, Call("missing", [])));
            // A Reply's body is a view valid until the next message is read, and the two handlers may finish in either
            // order: take each reply in before reading the next.
            TakenReply first = TakenReply.Of(await host.ReadAsync<Reply>());
            TakenReply second = TakenReply.Of(await host.ReadAsync<Reply>());
            (TakenReply echoed, TakenReply failed) = first.CallbackId == 7 ? (first, second) : (second, first);

            Assert.Equal(new byte[] { 1, 2 }, echoed.Body);
            Assert.Equal((Status)40_000, failed.Status);
            Assert.NotNull(failed.Error);
            Assert.Equal(("no such channel", "channel"), (failed.Error.Message, failed.Error.Detail));
        }
    }

    // §9.8: the read loop goes on while a handler runs, so the handler's own request on the same client is answered.
    [Fact]
    public async Task AHandlersOwnRequestIsAnsweredWhileItRuns()
    {
        LocalClient? self = null;
        var handler = new Handler(async call =>
        {
            LocalResult<Identity> identity = await self!.SendAsync(new GetIdentity(), CancellationToken.None);
            return CallReply.Ok(System.Text.Encoding.UTF8.GetBytes(identity.Value.UserName));
        });
        (LocalClient client, ScriptedHost host) = await ConnectAsync(handler, TimeProvider.System);
        self = client;
        await using (host)
        await using (client)
        {
            await host.SendAsync(new Callback(1, Call("whoami", [])));
            (uint correlation, GetIdentity _) = await host.ReadRequestAsync<GetIdentity>();
            await host.SendAsync(Response.Ok<GetIdentity, Identity>(correlation, ScriptedHost.Identity));
            Reply reply = await host.ReadAsync<Reply>();

            Assert.True(reply.TryReadOk<ForwardedCall, ReadOnlyMemory<byte>>(out ReadOnlyMemory<byte> body));
            Assert.Equal("alice", System.Text.Encoding.UTF8.GetString(body.Span));
        }
    }

    // §9.8: a handler that throws answers IoError, with the fault's type in the Error's detail.
    [Fact]
    public async Task AHandlerThatThrowsAnswersIoErrorNamingTheFaultsType()
    {
        var handler = new Handler(_ => throw new InvalidOperationException("the index is closed"));
        (LocalClient client, ScriptedHost host) = await ConnectAsync(handler, TimeProvider.System);
        await using (host)
        await using (client)
        {
            await host.SendAsync(new Callback(3, Call("timeline", [])));
            Reply reply = await host.ReadAsync<Reply>();

            Assert.Equal(Status.IoError, reply.Status);
            Assert.True(reply.TryReadError(out Error? error));
            Assert.Equal(typeof(InvalidOperationException).FullName, error.Detail);
            Assert.Contains("the index is closed", error.Message, StringComparison.Ordinal);
        }
    }

    // §9.8, §3.6: at the 60-second deadline the handler's token is cancelled; a handler that stops so answers nothing, since
    // the Host already told its caller ServiceTimeout.
    [Fact]
    public async Task AHandlerStoppedByItsDeadlineAnswersNothing()
    {
        var clock = new FakeTimeProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (call, deadline) =>
        {
            started.TrySetResult();
            await new TaskCompletionSource().Task.WaitAsync(deadline);
            return CallReply.Ok(ReadOnlyMemory<byte>.Empty);
        });
        (LocalClient client, ScriptedHost host) = await ConnectAsync(handler, clock);
        await using (host)
        await using (client)
        {
            await host.SendAsync(new Callback(5, Call("slow", [])));
            await started.Task.WaitAsync(Patience);
            clock.Advance(TimeSpan.FromSeconds(Limits.ServiceCallSeconds));
            await UntilAsync(() => client.CallbacksPending == 0);
            ValueTask<LocalResult<Identity>> asked = client.SendAsync(new GetIdentity(), CancellationToken.None);

            (uint correlation, GetIdentity _) = await host.ReadRequestAsync<GetIdentity>();
            await host.SendAsync(Response.Ok<GetIdentity, Identity>(correlation, ScriptedHost.Identity));
            Assert.True((await asked.AsTask().WaitAsync(Patience)).IsOk);
        }
    }

    // §3.6: a handler that ignores its deadline and returns after 120 seconds sends nothing: the Host may have forgotten
    // the id, and a Reply naming it would then be a violation.
    [Fact]
    public async Task NoReplyLeavesMoreThan120SecondsAfterItsCallbackArrived()
    {
        var clock = new FakeTimeProvider();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async _ =>
        {
            started.TrySetResult();
            await gate.Task;
            return CallReply.Ok(new byte[] { 1 });
        });
        (LocalClient client, ScriptedHost host) = await ConnectAsync(handler, clock);
        await using (host)
        await using (client)
        {
            await host.SendAsync(new Callback(6, Call("stubborn", [])));
            await started.Task.WaitAsync(Patience);

            // Two steps with a Ping between, so that the client's 120-second idle backstop (§4.4) never fires.
            clock.Advance(TimeSpan.FromSeconds(61));
            await host.SendAsync(new Ping { Nonce = 1 });
            Assert.Equal(1ul, (await host.ReadAsync<Pong>()).Nonce);
            clock.Advance(TimeSpan.FromSeconds(60));
            gate.SetResult();
            await UntilAsync(() => client.CallbacksPending == 0);
            await host.SendAsync(new Ping { Nonce = 2 });

            Assert.Equal(2ul, (await host.ReadAsync<Pong>()).Nonce);
        }
    }

    // §1.5, §9.8: a client that serves no callbacks answers each UnknownRequest and keeps the connection.
    [Fact]
    public async Task WithoutAHandlerEveryCallbackIsAnsweredUnknownRequest()
    {
        (LocalClient client, ScriptedHost host) = await ConnectAsync(null, TimeProvider.System);
        await using (host)
        await using (client)
        {
            await host.SendAsync(new Callback(9, Call("anything", [])));
            Reply reply = await host.ReadAsync<Reply>();

            Assert.Equal((9u, Status.UnknownRequest), (reply.CallbackId, reply.Status));
            Assert.False(client.Closed.IsCompleted);
        }
    }

    private static ForwardedCall Call(string method, byte[] payload) => new() { Client = 4, ClientName = "app", Method = method, Payload = payload };

    private static Task<(LocalClient Client, ScriptedHost Host)> ConnectAsync(CallbackHandler? handler, TimeProvider clock) =>
        ScriptedHost.ConnectAsync(new LocalClientOptions { Role = Role.Service, Name = "chat", Callbacks = handler, TimeProvider = clock }, ScriptedHost.Ack(Role.Service));

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(Patience);
        while (!condition())
        {
            deadline.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private sealed class Handler(Func<ForwardedCall, CancellationToken, Task<CallReply>> answer) : CallbackHandler
    {
        public Handler(Func<ForwardedCall, Task<CallReply>> answer)
            : this((call, _) => answer(call))
        {
        }

        protected internal override async ValueTask<CallReply> OnForwardedCallAsync(ForwardedCall call, CancellationToken deadline) => await answer(call, deadline);
    }

    // A Reply read off the wire, decoded at once: its OK body copied, or its Error.
    private sealed record TakenReply(uint CallbackId, Status Status, byte[]? Body, Error? Error)
    {
        public static TakenReply Of(Reply reply) => new(
            reply.CallbackId,
            reply.Status,
            reply.TryReadOk<ForwardedCall, ReadOnlyMemory<byte>>(out ReadOnlyMemory<byte> body) ? body.ToArray() : null,
            reply.TryReadError(out Error? error) ? error : null);
    }
}
