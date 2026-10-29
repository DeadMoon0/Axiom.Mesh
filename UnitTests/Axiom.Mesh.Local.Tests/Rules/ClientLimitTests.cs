using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local.Tests.Support;

namespace Axiom.Mesh.Local.Tests.Rules;

/// <summary>
/// The bounds the client keeps itself (LOCAL-SPEC.md §3.4, §7.4): it never sends a 65th outstanding request, and a
/// reader that falls 256 events behind loses that subscription only, as the Host's slow-consumer rule would decide.
/// </summary>
public sealed class ClientLimitTests
{
    [Fact]
    public async Task AClientRefusesA65thOutstandingRequestBeforeSendingIt()
    {
        (LocalClient client, ScriptedHost host) = await ScriptedHost.ConnectAsync();
        await using (host)
        await using (client)
        {
            var outstanding = new List<ValueTask<LocalResult<Store>>>();
            for (int i = 0; i < Limits.MaxOutstandingRequests; i++)
                outstanding.Add(client.SendAsync(new GetStore(), CancellationToken.None));

            var refused = await Assert.ThrowsAsync<RequestLimitException>(async () => await client.SendAsync(new GetStore(), CancellationToken.None));
            Assert.Equal(MeshFault.Caller, refused.Fault);
            Assert.True(refused.IsTransient);

            // Exactly 64 reached the Host; answering one frees a slot.
            var correlations = new HashSet<uint>();
            for (int i = 0; i < Limits.MaxOutstandingRequests; i++)
                correlations.Add((await host.ReadRequestAsync<GetStore>()).Correlation);
            Assert.Equal(Limits.MaxOutstandingRequests, correlations.Count);
            Assert.DoesNotContain(0U, correlations);

            // Correlations are handed out from 1 in order, so the first request sent is correlation 1.
            const uint first = 1;
            Assert.Contains(first, correlations);
            await host.SendAsync(Response.Ok<GetStore, Store>(first, Samples.Of<Store>()));
            Assert.True((await outstanding[0]).IsOk);
            ValueTask<LocalResult<Store>> next = client.SendAsync(new GetStore(), CancellationToken.None);
            (uint reused, _) = await host.ReadRequestAsync<GetStore>();
            Assert.DoesNotContain(reused, OthersThan(correlations, first));
            await host.SendAsync(Response.Ok<GetStore, Store>(reused, Samples.Of<Store>()));
            Assert.True((await next).IsOk);
        }
    }

    [Fact]
    public async Task AReaderThatFallsBehindLapsesOnlyItsOwnSubscription_OthersAndRequestsContinue()
    {
        (LocalClient client, ScriptedHost host) = await ScriptedHost.ConnectAsync();
        await using (host)
        await using (client)
        {
            Subscription<EntryStored> slow = await SubscribeAsync<EntryStored>(client, host);
            Subscription<StoreChanged> other = await SubscribeAsync<StoreChanged>(client, host);

            const int sent = Limits.MaxQueuedEvents + 44;
            for (ulong seq = 1; seq <= sent; seq++)
                await host.SendAsync(new EventFrame(slow.Id, new EntryStored { Entry = Samples.Of<Entry>() with { StoreSeq = seq } }));
            await host.SendAsync(new EventFrame(other.Id, new StoreChanged { Store = Samples.Of<Store>() }));

            // The client told the Host with Unsubscribe; the frames after it on the pipe were read before its response.
            (uint unsubscribe, Unsubscribe request) = await host.ReadRequestAsync<Unsubscribe>();
            Assert.Equal(slow.Id, request.SubscriptionId);
            await host.SendAsync(Response.Ok<Unsubscribe, NoContent>(unsubscribe, default));
            ValueTask<LocalResult<Identity>> identity = client.SendAsync(new GetIdentity(), CancellationToken.None);
            (uint asked, _) = await host.ReadRequestAsync<GetIdentity>();
            await host.SendAsync(Response.Ok<GetIdentity, Identity>(asked, ScriptedHost.Identity));
            Assert.True((await identity).IsOk);

            int delivered = 0;
            await foreach (EntryStored stored in slow)
                Assert.Equal((ulong)++delivered, stored.Entry.StoreSeq);
            Assert.Equal(Limits.MaxQueuedEvents, delivered);
            Assert.Equal((uint)(sent - Limits.MaxQueuedEvents), slow.Lapse!.Dropped);
            Assert.Equal((ulong)Limits.MaxQueuedEvents, slow.Lapse.LastSeq);

            await foreach (StoreChanged changed in other)
            {
                Assert.NotNull(changed.Store);
                break;
            }

            Assert.False(other.IsEnded);
        }
    }

    private static async Task<Subscription<TEvent>> SubscribeAsync<TEvent>(LocalClient client, ScriptedHost host)
        where TEvent : LocalEvent, IEvent<TEvent>
    {
        ValueTask<LocalResult<Subscription<TEvent>>> subscribing = client.SubscribeAsync<TEvent>(Filter.Any, CancellationToken.None);
        (uint correlation, _) = await host.ReadRequestAsync<Subscribe>();
        await host.SendAsync(Response.Ok<Subscribe, NoContent>(correlation, default));
        return (await subscribing).Value;
    }

    private static HashSet<uint> OthersThan(HashSet<uint> values, uint except)
    {
        var others = new HashSet<uint>(values);
        others.Remove(except);
        return others;
    }
}
