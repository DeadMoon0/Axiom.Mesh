using System.Collections.Generic;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// One subscription of one connection (LOCAL-SPEC.md §7): its topic and filter, its own queue bounded to 256 events and
/// 16 MiB (§7.4), the store sequences a replay still has to read (§7.2), and where it stands. Changed only under its
/// connection's outbox lock.
/// </summary>
internal sealed class LocalSubscription
{
    /// <summary>A new subscription, not yet answered.</summary>
    /// <param name="id">The client's id.</param>
    /// <param name="topic">The topic.</param>
    /// <param name="filter">The filter, checked.</param>
    /// <param name="liveAfter">Topic entries: live events at or below this store sequence are the replay's, or older than the subscription.</param>
    /// <param name="replay">Topic entries: the store sequences to replay, ascending, at most 256.</param>
    public LocalSubscription(uint id, Topic topic, Filter filter, ulong liveAfter, IReadOnlyList<ulong> replay)
    {
        Id = id;
        Topic = topic;
        Filter = filter;
        LiveAfter = liveAfter;
        Replay = new Queue<ulong>(replay);
    }

    /// <summary>The client's id.</summary>
    public uint Id { get; }

    /// <summary>The topic.</summary>
    public Topic Topic { get; }

    /// <summary>The filter.</summary>
    public Filter Filter { get; }

    /// <summary>Topic entries: the newest store sequence when it subscribed; live events at or below it are not delivered live.</summary>
    public ulong LiveAfter { get; }

    /// <summary>The store sequences a replay has still to deliver, read one at a time as the queue drains.</summary>
    public Queue<ulong> Replay { get; }

    /// <summary>The events queued, not yet handed to the pipe.</summary>
    public Queue<QueuedEvent> Queue { get; } = new();

    /// <summary>Encoded bytes of <see cref="Queue"/>.</summary>
    public long QueuedBytes { get; set; }

    /// <summary>Whether the <c>Response</c> to its <c>Subscribe</c> was written, so its events may follow (§7.1).</summary>
    public bool Answered { get; set; }

    /// <summary>Whether it lapsed: only the queued <c>SubscriptionLapsed</c> is left to write, and no event is taken.</summary>
    public bool Lapsed { get; set; }

    /// <summary>Topic entries: the store sequence of the last <c>EntryStored</c> handed to the pipe.</summary>
    public ulong LastSeqHanded { get; set; }

    /// <summary>Whether the writer has something of it to write now.</summary>
    public bool HasWork => Answered && (Queue.Count != 0 || (!Lapsed && Replay.Count != 0));

    /// <inheritdoc />
    public override string ToString() => $"#{Id} {Topic} queued {Queue.Count}/{QueuedBytes} B replay {Replay.Count}{(Lapsed ? " lapsed" : string.Empty)}";
}
