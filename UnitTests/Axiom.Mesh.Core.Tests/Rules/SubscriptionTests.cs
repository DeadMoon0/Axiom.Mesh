using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §7: the response to <c>Subscribe</c> precedes the subscription's first event and no event follows the
/// response to its <c>Unsubscribe</c>; a replay from a store sequence delivers exactly the stored Entries from there, once
/// and in order, then goes live, and more than 256 is <c>GapTooLarge</c>; a subscription's queue lapses alone at a 257th
/// event or 16 MiB, and a connection that holds 64 MiB its client does not read is closed <c>Bye(Unresponsive)</c>.
/// </summary>
public sealed class SubscriptionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task TheResponseToSubscribePrecedesItsFirstEvent_AndNoEventFollowsTheUnsubscribeResponse()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        PackageId channel = await node.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        await using RawLocalClient client = await RawLocalClient.ReadyAsync(mesh, LocalMeshes.Node);

        // Entries are stored while the subscription is made and ended, so events are ready the moment either is.
        using var writing = new CancellationTokenSource();
        Task writer = Task.Run(async () =>
        {
            for (int i = 0; !writing.IsCancellationRequested && i < 200; i++)
                await node.WriteEntryAsync(channel, ObjectFixtures.Message("hello " + i), None);
        });

        uint subscribe = await client.RequestAsync(new Subscribe { SubscriptionId = 3, Topic = Topic.Entries, Filter = Filter.Any });
        var order = new List<string>();
        while (order.Count(o => o == "event") < 5)
        {
            IHostMessage? message = await client.ReadAsync();
            if (message is Response { Correlation: var c } && c == subscribe)
                order.Add("response");
            else if (message is EventFrame { SubscriptionId: 3 })
                order.Add("event");
        }

        Assert.Equal("response", order[0]);
        uint unsubscribe = await client.RequestAsync(new Unsubscribe { SubscriptionId = 3 });
        while (await client.ReadAsync() is not Response { Correlation: var u } || u != unsubscribe)
        {
        }

        uint marker = await client.RequestAsync(new GetIdentity());
        while (true)
        {
            IHostMessage? message = await client.ReadAsync();
            Assert.False(message is EventFrame { SubscriptionId: 3 }, "an event followed the response to Unsubscribe");
            if (message is Response { Correlation: var m } && m == marker)
                break;
        }

        await writing.CancelAsync();
        await writer;
    }

    [Fact]
    public async Task FromSeqDeliversExactlyTheEntriesWithStoreSeqAtLeastFromSeqOnceInOrderThenLive_Over256IsGapTooLarge()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        PackageId channel = await node.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        PackageId other = await node.CreatePackageAsync("Work", PackageKind.Channel, Retention.Default, None);
        for (int i = 1; i <= 10; i++)
            await node.WriteEntryAsync(i % 3 == 0 ? other : channel, ObjectFixtures.Message("stored " + i), None);

        LocalClient client = await node.ConnectUserAsync("index", None);
        Subscription<EntryStored> replay = (await client.SubscribeAsync<EntryStored>(Filter.Any with { PackageId = channel, FromSeq = 4 }, None)).Value;
        for (int i = 11; i <= 13; i++)
            await node.WriteEntryAsync(channel, ObjectFixtures.Message("live " + i), None);

        // Store sequences 1..13: 3, 6 and 9 are the other Package's; from 4 on, the channel's are 4, 5, 7, 8, 10, then live 11–13.
        List<ulong> seen = await FirstAsync(replay, 8);
        Assert.Equal([4ul, 5, 7, 8, 10, 11, 12, 13], seen);

        for (int i = 14; i <= Limits.MaxReplayEntries + 1; i++)
            await node.WriteEntryAsync(channel, ObjectFixtures.Message("many " + i), None);
        LocalResult<Subscription<EntryStored>> exactly = await client.SubscribeAsync<EntryStored>(Filter.Any with { FromSeq = 2 }, None);
        Assert.True(exactly.IsOk, "256 stored Entries from store sequence 2 replay");
        LocalResult<Subscription<EntryStored>> tooMany = await client.SubscribeAsync<EntryStored>(Filter.Any with { FromSeq = 1 }, None);
        Assert.Equal(Status.GapTooLarge, tooMany.Status);
        Assert.Equal((ulong)Limits.MaxReplayEntries, (await FirstAsync(exactly.Value, Limits.MaxReplayEntries)).Count == Limits.MaxReplayEntries ? (ulong)Limits.MaxReplayEntries : 0ul);
    }

    [Fact]
    public async Task A257thEventOr16MiBLapsesOnlyThatSubscription_OthersAndRequestsContinue_64MiBUnacceptedIsByeUnresponsive()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];

        // A client that reads nothing yet: what the pipe does not take waits in the subscriptions' queues.
        await using RawLocalClient client = await RawLocalClient.ReadyAsync(mesh, LocalMeshes.Node);
        uint first = await client.RequestAsync(new Subscribe { SubscriptionId = 1, Topic = Topic.Entries, Filter = Filter.Any });
        uint second = await client.RequestAsync(new Subscribe { SubscriptionId = 2, Topic = Topic.Users, Filter = Filter.Any });
        LocalConnection connection = Assert.Single(node.Node.Local.Book.Live());
        await Waits.UntilAsync(() => connection.Outbox.Subscriptions == 2, "both subscriptions");

        // The pipe fills with 64 KiB of events; then 256 more wait in the queue, and the 257th lapses it.
        var big = Event(16 * 1024, 1);
        var bigQueued = new QueuedEvent(big, FrameSizes.OfEvent(big), 1);
        int offered = 0;
        while (connection.Outbox.Of(Topic.Entries).Length == 1)
        {
            connection.Offer(Topic.Entries, _ => true, bigQueued with { StoreSeq = (ulong)++offered + 1_000 });
            await Task.Yield();
        }

        var user = new UserChanged { Change = UserChange.Known, User = new User { Name = "bob" } };
        connection.Offer(Topic.Users, _ => true, new QueuedEvent(user, FrameSizes.OfEvent(user), 0));

        var messages = new List<IHostMessage>();
        uint after = await client.RequestAsync(new GetIdentity());
        while (true)
        {
            IHostMessage? message = await client.ReadAsync();
            Assert.NotNull(message);
            messages.Add(message);
            bool answered = messages.OfType<Response>().Any(r => r.Correlation == after);
            bool lapsedSeen = messages.OfType<EventFrame>().Any(e => e.Event is SubscriptionLapsed);
            bool userSeen = messages.OfType<EventFrame>().Any(e => e.Event is UserChanged);
            if (answered && lapsedSeen && userSeen)
                break;
        }

        SubscriptionLapsed lapsed = Assert.Single(messages.OfType<EventFrame>().Select(e => e.Event).OfType<SubscriptionLapsed>());
        Assert.Equal((uint)Limits.MaxQueuedEvents + 1, lapsed.Dropped);
        Assert.Contains(messages.OfType<EventFrame>(), e => e.SubscriptionId == 2 && e.Event is UserChanged);
        Assert.Contains(messages.OfType<Response>(), r => r.Correlation == first && r.IsOk);
        Assert.Contains(messages.OfType<Response>(), r => r.Correlation == second && r.IsOk);
        Assert.Equal(1, connection.Outbox.Subscriptions);

        // 16 MiB: events of 1 MiB lapse a subscription at the 16th, whatever the count.
        uint third = await client.RequestAsync(new Subscribe { SubscriptionId = 3, Topic = Topic.Entries, Filter = Filter.Any });
        Assert.True((await client.ResponseAsync(third)).IsOk);
        var huge = Event(1_048_000, 2);
        var hugeQueued = new QueuedEvent(huge, FrameSizes.OfEvent(huge), 5_000);
        int count = 0;
        while (connection.Outbox.Of(Topic.Entries).Length == 1)
            connection.Offer(Topic.Entries, _ => true, hugeQueued with { StoreSeq = 5_000 + (ulong)++count });
        Assert.True(count <= 16 + 64, $"lapsed at {count} events of 1 MiB, the pipe taking some");

        // 64 MiB held across subscriptions that each stay under 16 MiB: the connection is closed Bye(Unresponsive).
        for (uint id = 10; id < 16; id++)
        {
            uint subscribe = await client.RequestAsync(new Subscribe { SubscriptionId = id, Topic = Topic.Entries, Filter = Filter.Any });
            Assert.True((await client.ResponseAsync(subscribe)).IsOk);
        }

        for (int round = 0; round < 12 && node.Node.Local.Connections == 1; round++)
            connection.Offer(Topic.Entries, _ => true, hugeQueued with { StoreSeq = 9_000 + (ulong)round });
        await Waits.UntilAsync(() => node.Node.Local.Connections == 0, "the connection closed for holding 64 MiB");
        Assert.Contains(node.Node.Facts.Recent(), f => f.Kind == NodeFactKind.LocalEnded && f.Detail.Contains("64 MiB", StringComparison.Ordinal));
    }

    private static EntryStored Event(int payload, byte fill) => new()
    {
        Entry = new Entry { StoreSeq = 1, Payload = Enumerable.Repeat(fill, payload).ToArray(), Kind = EntryKind.Message },
    };

    private static async Task<List<ulong>> FirstAsync(Subscription<EntryStored> events, int count)
    {
        var seen = new List<ulong>();
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        await foreach (EntryStored stored in events.WithCancellation(deadline.Token))
        {
            seen.Add(stored.Entry.StoreSeq);
            if (seen.Count == count)
                break;
        }

        return seen;
    }
}
