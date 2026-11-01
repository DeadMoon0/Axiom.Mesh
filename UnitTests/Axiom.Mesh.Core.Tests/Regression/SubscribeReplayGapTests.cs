using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Core.Tests.Regression;

/// <summary>
/// The seam between a <c>Subscribe</c>'s replay and its live events on topic entries (LOCAL-SPEC.md §7.2,
/// <c>SubscriptionRequests</c> and <c>LocalEventHub.EntriesGate</c>): the newest store sequence is read, the replay planned
/// and the subscription added under the gate the hub offers Entries under.
/// </summary>
public sealed class SubscribeReplayGapTests
{
    private const int Written = 200;
    private const int Subscriptions = 24;

    private static readonly CancellationToken None = CancellationToken.None;

    // Damage: an Entry stored while a Subscribe planned its replay (an asynchronous read of the index) was above the store
    // sequence the replay ended at, and the hub offered it before the subscription was added: it reached nobody, and a
    // service that caught up this way missed it for good (found by the service runtime's catch-up tests under load).
    [Fact]
    public async Task SubscriptionsMadeWhileEntriesAreStoredSeeEveryEntryFromTheirFromSeqWithoutAGap()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        PackageId channel = await node.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        LocalClient app = await node.ConnectUserAsync("app", None);

        Task writing = Task.Run(async () =>
        {
            for (int i = 0; i < Written; i++)
                await node.WriteEntryAsync(channel, new MessagePayload { Text = "m" + i, Attachments = [], ReplyTo = default }, None);
        });
        var made = new List<(ulong From, Subscription<EntryStored> Events)>();
        for (int i = 0; i < Subscriptions && !writing.IsCompleted; i++)
        {
            ulong from = node.Node.Writer.Totals.StoreSeq + 1;
            made.Add((from, (await app.SubscribeAsync<EntryStored>(Filter.Any with { FromSeq = from }, None)).Value));
        }

        await writing;
        foreach ((ulong from, Subscription<EntryStored> events) in made)
        {
            var seen = new List<ulong>();
            if (from <= Written)
                await Events.FirstAsync(events, e => { seen.Add(e.Entry.StoreSeq); return e.Entry.StoreSeq == Written; });
            Assert.Equal(Enumerable.Range((int)from, Math.Max(0, Written - (int)from + 1)).Select(s => (ulong)s), seen);
        }
    }
}
