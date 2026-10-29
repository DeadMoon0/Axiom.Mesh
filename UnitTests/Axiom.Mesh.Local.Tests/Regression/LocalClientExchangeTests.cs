using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local.Tests.Support;

namespace Axiom.Mesh.Local.Tests.Regression;

/// <summary>
/// <c>LocalClient</c> against a scripted Host over an in-memory pipe pair: <c>Hello</c>, requests and responses,
/// subscriptions, a lapse, <c>Ping</c>, <c>Drain</c> and <c>Bye</c>, frame by frame as LOCAL-SPEC.md §4 and §7 lay them out.
/// </summary>
public sealed class LocalClientExchangeTests
{
    // Damage: a client that sent anything but its Hello first, or lost the HelloAck's identity, epoch or sequence, could
    // not connect to any Host or could not catch up its index.
    [Fact]
    public async Task OpeningSaysHelloWithTheOptionsAndKeepsTheHelloAck()
    {
        var options = new LocalClientOptions { Role = Role.Service, Name = "chat", ClientVersion = "1.0", Token = ServiceToken.FromBytes(new byte[32]) };
        (LocalClient client, ScriptedHost host) = await ScriptedHost.ConnectAsync(options, ScriptedHost.Ack(Role.Service));
        await using (host)
        await using (client)
        {
            Assert.Equal(Role.Service, host.Hello.Role);
            Assert.Equal("chat", host.Hello.Name);
            Assert.Equal("1.0", host.Hello.ClientVersion);
            Assert.Equal(Role.Service, client.Host.Role);
            Assert.Equal(ScriptedHost.Identity, client.Host.Identity);
            Assert.Equal(4_120UL, client.Host.StoreSeq);
        }
    }

    // Damage: a client that matched responses by order instead of correlation, or turned a refusal into an exception,
    // would hand one request's answer to another and force every caller to catch.
    [Fact]
    public async Task ARequestIsAnsweredWithItsOkBodyAndARefusalIsAValue()
    {
        (LocalClient client, ScriptedHost host) = await ScriptedHost.ConnectAsync();
        await using (host)
        await using (client)
        {
            ValueTask<LocalResult<Identity>> identity = client.SendAsync(new GetIdentity(), CancellationToken.None);
            ValueTask<LocalResult<Package>> package = client.SendAsync(new GetPackage { PackageId = new PackageId(Samples.Hash(3)) }, CancellationToken.None);

            (uint first, _) = await host.ReadRequestAsync<GetIdentity>();
            (uint second, GetPackage asked) = await host.ReadRequestAsync<GetPackage>();
            Assert.NotEqual(first, second);
            Assert.Equal(new PackageId(Samples.Hash(3)), asked.PackageId);

            // Answered out of order, as §3.4 allows.
            await host.SendAsync(Response.Failure(second, Status.NotFound, new Error { Message = "no such Package", Detail = "packageId" }));
            await host.SendAsync(Response.Ok<GetIdentity, Identity>(first, ScriptedHost.Identity));

            LocalResult<Identity> gotIdentity = await identity;
            LocalResult<Package> gotPackage = await package;
            Assert.True(gotIdentity.IsOk);
            Assert.Equal(ScriptedHost.Identity, gotIdentity.Value);
            Assert.False(gotPackage.IsOk);
            Assert.Equal(Status.NotFound, gotPackage.Status);
            Assert.Equal("packageId", gotPackage.Error.Detail);
        }
    }

    // Damage: a subscription that delivered out of order, dropped events, or kept running after the Host lapsed it would
    // leave an index with holes and no way to know where to resume.
    [Fact]
    public async Task ASubscriptionDeliversInOrderAndEndsWithTheHostsLapseAndWhereToResume()
    {
        (LocalClient client, ScriptedHost host) = await ScriptedHost.ConnectAsync();
        await using (host)
        await using (client)
        {
            ValueTask<LocalResult<Subscription<EntryStored>>> subscribing = client.SubscribeAsync<EntryStored>(new Filter { FromSeq = 4_101 }, CancellationToken.None);
            (uint correlation, Subscribe subscribe) = await host.ReadRequestAsync<Subscribe>();
            Assert.Equal(Topic.Entries, subscribe.Topic);
            Assert.Equal(4_101UL, subscribe.Filter.FromSeq);
            await host.SendAsync(Response.Ok<Subscribe, NoContent>(correlation, default));
            Subscription<EntryStored> subscription = (await subscribing).Value;

            for (ulong seq = 4_101; seq <= 4_103; seq++)
                await host.SendAsync(new EventFrame(subscribe.SubscriptionId, new EntryStored { Entry = Samples.Of<Entry>() with { StoreSeq = seq } }));
            await host.SendAsync(new EventFrame(subscribe.SubscriptionId, new SubscriptionLapsed { Dropped = 257, LastSeq = 4_103 }));

            var seen = new List<ulong>();
            await foreach (EntryStored stored in subscription)
                seen.Add(stored.Entry.StoreSeq);

            Assert.Equal([4_101UL, 4_102UL, 4_103UL], seen);
            Assert.True(subscription.IsEnded);
            Assert.Equal(257U, subscription.Lapse!.Dropped);
            Assert.Equal(4_103UL, subscription.Lapse.LastSeq);
        }
    }

    // Damage: Unsubscribe that did not end the enumeration would leave a reader waiting for ever.
    [Fact]
    public async Task UnsubscribingEndsTheEnumerationWithoutALapse()
    {
        (LocalClient client, ScriptedHost host) = await ScriptedHost.ConnectAsync();
        await using (host)
        await using (client)
        {
            ValueTask<LocalResult<Subscription<PackageChanged>>> subscribing = client.SubscribeAsync<PackageChanged>(Filter.Any, CancellationToken.None);
            (uint correlation, Subscribe subscribe) = await host.ReadRequestAsync<Subscribe>();
            Assert.Equal(Topic.Packages, subscribe.Topic);
            await host.SendAsync(Response.Ok<Subscribe, NoContent>(correlation, default));
            Subscription<PackageChanged> subscription = (await subscribing).Value;

            ValueTask<LocalResult<NoContent>> unsubscribing = client.UnsubscribeAsync(subscription, CancellationToken.None);
            (uint unsubscribe, Unsubscribe request) = await host.ReadRequestAsync<Unsubscribe>();
            Assert.Equal(subscribe.SubscriptionId, request.SubscriptionId);
            await host.SendAsync(Response.Ok<Unsubscribe, NoContent>(unsubscribe, default));
            Assert.True((await unsubscribing).IsOk);

            await foreach (PackageChanged unexpected in subscription)
                Assert.Fail($"No event follows the Unsubscribe response, but {unexpected} did.");
            Assert.Null(subscription.Lapse);
        }
    }

    // Damage: a client that did not answer Ping would be closed as Unresponsive after 90 seconds of quiet (§4.4).
    [Fact]
    public async Task TheHostsPingIsAnsweredWithoutTheApplication()
    {
        (LocalClient client, ScriptedHost host) = await ScriptedHost.ConnectAsync();
        await using (host)
        await using (client)
        {
            await host.SendAsync(new Ping { Nonce = 77 });

            Pong pong = await host.ReadAsync<Pong>();
            Assert.Equal(77UL, pong.Nonce);
        }
    }

    // Damage: a Drain the client did not surface would leave it writing until the Host cut it off with Bye(Shutdown).
    [Fact]
    public async Task ADrainCancelsTheDrainingToken_AndByeSaysDone()
    {
        (LocalClient client, ScriptedHost host) = await ScriptedHost.ConnectAsync();
        await using (host)
        {
            var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = client.Draining.Register(() => drained.TrySetResult());
            await host.SendAsync(new Drain { GraceMilliseconds = 5_000 });
            await drained.Task.WaitAsync(TimeSpan.FromSeconds(30));

            await client.ByeAsync(CancellationToken.None);
            Bye bye = await host.ReadAsync<Bye>();
            Assert.Equal(ByeReason.Done, bye.Reason);
            Assert.Equal(MeshFault.Caller, (await client.Closed).Fault);
        }
    }

    // Damage: outstanding requests that waited for ever after the Host left, or a closed client that still accepted
    // requests, would hang every caller of a Host that stopped.
    [Fact]
    public async Task AHostByeFailsOutstandingRequestsAndEveryLaterOne()
    {
        (LocalClient client, ScriptedHost host) = await ScriptedHost.ConnectAsync();
        await using (host)
        await using (client)
        {
            ValueTask<LocalResult<Store>> store = client.SendAsync(new GetStore(), CancellationToken.None);
            await host.ReadRequestAsync<GetStore>();
            await host.SendAsync(Bye.Of(ByeReason.Shutdown, "stopping"));

            LocalConnectionException failed = await Assert.ThrowsAsync<LocalConnectionException>(async () => await store);
            Assert.Equal(ByeReason.Shutdown, failed.Reason);
            Assert.True(failed.IsTransient);
            Assert.Equal(ByeReason.Shutdown, (await client.Closed).Reason);
            await Assert.ThrowsAsync<LocalConnectionException>(async () => await client.SendAsync(new GetStore(), CancellationToken.None));
        }
    }
}
