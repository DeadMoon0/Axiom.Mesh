using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local.Tests.Support;

namespace Axiom.Mesh.Local.Tests.Regression;

/// <summary>
/// The callback dispatch of <see cref="LocalClient"/> (LOCAL-SPEC.md §3.6): the slot a callback id holds is removed by
/// whoever answers, and only that one sends, so a callback is answered once however many handlers ran for its id.
/// </summary>
public sealed class CallbackReplyOnceTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    // Damage: a callback id that arrived twice was answered twice; the Host had already completed it with the first Reply,
    // so the second named an id no longer outstanding and, once the 5-minute window passed, closed the service as a
    // violation.
    [Fact]
    public async Task ACallbackIdThatArrivesTwiceIsAnsweredOnce()
    {
        var handler = new Counting();
        (LocalClient client, ScriptedHost host) = await ScriptedHost.ConnectAsync(
            new LocalClientOptions { Role = Role.Service, Name = "chat", Callbacks = handler },
            ScriptedHost.Ack(Role.Service));
        await using (host)
        await using (client)
        {
            var call = new ForwardedCall { Client = 1, ClientName = "app", Method = "count" };
            await host.SendAsync(new Callback(11, call));
            await host.SendAsync(new Callback(11, call));
            await UntilAsync(() => handler.Calls == 2);
            handler.Release();
            Reply reply = await host.ReadAsync<Reply>();
            await UntilAsync(() => client.CallbacksPending == 0 && handler.Returned == 2);
            await host.SendAsync(new Ping { Nonce = 3 });

            Assert.Equal(11u, reply.CallbackId);
            Assert.Equal(3ul, (await host.ReadAsync<Pong>()).Nonce);
        }
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(Patience);
        while (!condition())
        {
            deadline.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    // Both handlers start before either returns, so both run while the id's slot is held.
    private sealed class Counting : CallbackHandler
    {
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;
        private int returned;

        public int Calls => Volatile.Read(ref calls);

        public int Returned => Volatile.Read(ref returned);

        public void Release() => gate.SetResult();

        protected internal override async ValueTask<CallReply> OnForwardedCallAsync(ForwardedCall call, CancellationToken deadline)
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            Interlocked.Increment(ref returned);
            return CallReply.Ok(ReadOnlyMemory<byte>.Empty);
        }
    }
}
