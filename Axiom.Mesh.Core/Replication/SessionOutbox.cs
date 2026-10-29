using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Core;

/// <summary>
/// What one session has to send, in two lanes (SPEC.md §7.6): the control lane for every message but <c>Object</c>,
/// unbounded and small, always served first; and the object lane, at most 8 MiB of object bytes, behind which further
/// answers to <c>Want</c> are deferred until it drains. A <c>NotFound</c> is never deferred. It also keeps the count of
/// the peer's wants this side has not yet answered (§7.5), which the answers settle.
/// </summary>
/// <remarks>
/// Every method returns at once; the session's write task takes the items one by one with <see cref="TakeAsync"/> and
/// reports each one done with <see cref="Done"/>. A <c>Have</c> is dropped when more than 65 536 claimed hashes already
/// wait, so a slow peer costs bounded memory; nothing else is ever dropped (plan §3.6). Thread-safe.
/// </remarks>
internal sealed class SessionOutbox : IDisposable
{
    /// <summary>The most hashes of <c>Have</c> that may wait on the control lane; beyond, a <c>Have</c> is dropped.</summary>
    public const int MaxQueuedHaveHashes = Limits.MaxRecordedSources;

    private readonly object gate = new();
    private readonly Queue<MeshMessage> control = new();
    private readonly LinkedList<OutboxItem> objects = new();
    private readonly LinkedList<OutboxItem> deferred = new();
    private readonly SemaphoreSlim ready = new(0);
    private ObjectQueueRule queue = ObjectQueueRule.Empty;
    private WantBudget wantsIn = WantBudget.Empty;
    private int haveHashes;
    private int busy;
    private bool completed;
    private long sent;

    /// <summary>Messages taken for writing so far, objects included.</summary>
    public long MessagesSent => Interlocked.Read(ref sent);

    /// <summary>Object bytes queued on the object lane (deferred answers not included).</summary>
    public long QueuedObjectBytes
    {
        get
        {
            lock (gate)
                return queue.QueuedBytes;
        }
    }

    /// <summary>Answers to <c>Want</c> deferred until the object lane drains.</summary>
    public int Deferred
    {
        get
        {
            lock (gate)
                return deferred.Count;
        }
    }

    /// <summary>Hashes the peer wants that this side has not yet answered (LOCAL-SPEC.md §5.8 <c>wantsIn</c>).</summary>
    public int WantsIn
    {
        get
        {
            lock (gate)
                return wantsIn.Outstanding;
        }
    }

    /// <summary>Whether nothing waits on either lane and nothing is being written.</summary>
    public bool IsIdle
    {
        get
        {
            lock (gate)
                return control.Count == 0 && objects.Count == 0 && deferred.Count == 0 && busy == 0;
        }
    }

    /// <summary>Puts a message on the control lane.</summary>
    /// <param name="message">The message.</param>
    public void Send(MeshMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (gate)
        {
            if (completed)
                return;
            control.Enqueue(message);
        }

        ready.Release();
    }

    /// <summary>Puts several messages on the control lane at once, nothing between them (a listing's pages, SPEC.md §7.2).</summary>
    /// <param name="messages">The messages, in order.</param>
    public void SendAll(IReadOnlyList<MeshMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        lock (gate)
        {
            if (completed)
                return;
            foreach (MeshMessage message in messages)
                control.Enqueue(message);
        }

        ready.Release(Math.Max(1, messages.Count));
    }

    /// <summary>Claims objects with <c>Have</c>, cut at 1 024 hashes a message; dropped when too many claims wait already.</summary>
    /// <param name="hashes">The objects.</param>
    /// <returns><see langword="false"/> when dropped.</returns>
    public bool TrySendHave(IReadOnlyList<ObjectHash> hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        if (hashes.Count == 0)
            return true;
        int messages = 0;
        lock (gate)
        {
            if (completed || haveHashes + hashes.Count > MaxQueuedHaveHashes)
                return false;
            haveHashes += hashes.Count;
            for (int at = 0; at < hashes.Count; at += Limits.MaxHaveHashes)
            {
                int count = Math.Min(Limits.MaxHaveHashes, hashes.Count - at);
                var part = new ObjectHash[count];
                for (int i = 0; i < count; i++)
                    part[i] = hashes[at + i];
                control.Enqueue(new Have(part));
                messages++;
            }
        }

        ready.Release(messages);
        return true;
    }

    /// <summary>Counts a <c>Want</c> the peer sent (SPEC.md §7.5).</summary>
    /// <param name="hashes">How many hashes it carries.</param>
    /// <returns><see langword="false"/> when more than 64 would be outstanding: a violation.</returns>
    public bool TryAcceptWant(int hashes)
    {
        lock (gate)
        {
            if (!wantsIn.TryWant(hashes, out WantBudget next))
                return false;
            wantsIn = next;
            return true;
        }
    }

    /// <summary>Answers one wanted hash with <c>NotFound</c> on the control lane, never deferred (SPEC.md §7.6, §7.7).</summary>
    /// <param name="hash">The hash.</param>
    public void NotFound(ObjectHash hash)
    {
        lock (gate)
        {
            wantsIn = wantsIn.Settle(Math.Min(1, wantsIn.Outstanding));
            if (completed)
                return;
            control.Enqueue(new NotFound(hash));
        }

        ready.Release();
    }

    /// <summary>Answers one wanted hash with its object: on the object lane while it fits in 8 MiB, else deferred.</summary>
    /// <param name="hash">The object.</param>
    /// <param name="size">Its length in bytes.</param>
    public void Answer(ObjectHash hash, int size)
    {
        bool queued;
        lock (gate)
        {
            if (completed)
                return;
            var item = new OutboxItem(null, hash, size);
            queued = deferred.Count == 0 && queue.TryQueue(size, out queue);
            if (queued)
                objects.AddLast(item);
            else
                deferred.AddLast(item);
        }

        if (queued)
            ready.Release();
    }

    /// <summary>
    /// The peer cancelled wants (SPEC.md §7.8): their answers still queued or deferred are dropped and settled. One being
    /// written is not.
    /// </summary>
    /// <param name="hashes">The hashes cancelled.</param>
    /// <returns>The answers dropped, so that their promises can be released.</returns>
    public IReadOnlyList<ObjectHash> Cancel(IReadOnlyList<ObjectHash> hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        var named = new HashSet<ObjectHash>(hashes);
        var dropped = new List<ObjectHash>();
        int promoted;
        lock (gate)
        {
            Drop(objects, named, dropped, true);
            Drop(deferred, named, dropped, false);
            wantsIn = wantsIn.Settle(Math.Min(dropped.Count, wantsIn.Outstanding));
            promoted = Promote();
        }

        if (promoted > 0)
            ready.Release(promoted);
        return dropped;
    }

    /// <summary>Waits for the next item: a control message first, else the next object to send.</summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The item, or <see langword="null"/> once the outbox is completed.</returns>
    public async ValueTask<OutboxItem?> TakeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (gate)
            {
                if (control.TryDequeue(out MeshMessage? message))
                {
                    if (message is Have have)
                        haveHashes -= have.Hashes.Count;
                    busy++;
                    sent++;
                    return new OutboxItem(message, default, 0);
                }

                if (objects.First is { } first)
                {
                    objects.RemoveFirst();
                    busy++;
                    sent++;
                    return first.Value;
                }

                if (completed)
                    return null;
            }

            await ready.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The write task is done with an item: an object leaves the 8 MiB, and deferred answers move up.</summary>
    /// <param name="item">The item <see cref="TakeAsync"/> gave.</param>
    public void Done(OutboxItem item)
    {
        int promoted = 0;
        lock (gate)
        {
            busy--;
            if (item.Message is null)
            {
                queue = queue.Dequeue(item.Size);
                wantsIn = wantsIn.Settle(Math.Min(1, wantsIn.Outstanding));
                promoted = Promote();
            }
        }

        if (promoted > 0)
            ready.Release(promoted);
    }

    /// <summary>Takes nothing more: the write task ends once it has taken what is queued, or at once when cancelled.</summary>
    /// <returns>The answers still queued or deferred, never sent, so that their promises can be released.</returns>
    public IReadOnlyList<ObjectHash> Complete()
    {
        var left = new List<ObjectHash>();
        lock (gate)
        {
            completed = true;
            foreach (OutboxItem item in objects)
                left.Add(item.Hash);
            foreach (OutboxItem item in deferred)
                left.Add(item.Hash);
            objects.Clear();
            deferred.Clear();
            control.Clear();
            queue = ObjectQueueRule.Empty;
        }

        ready.Release();
        return left;
    }

    /// <inheritdoc />
    public void Dispose() => ready.Dispose();

    // Deferred answers move to the object lane, oldest first, while they fit.
    private int Promote()
    {
        int promoted = 0;
        while (deferred.First is { } next && queue.TryQueue(next.Value.Size, out ObjectQueueRule grown))
        {
            queue = grown;
            deferred.RemoveFirst();
            objects.AddLast(next.Value);
            promoted++;
        }

        return promoted;
    }

    private void Drop(LinkedList<OutboxItem> lane, HashSet<ObjectHash> named, List<ObjectHash> dropped, bool queued)
    {
        LinkedListNode<OutboxItem>? node = lane.First;
        while (node is not null)
        {
            LinkedListNode<OutboxItem>? next = node.Next;
            if (named.Contains(node.Value.Hash))
            {
                lane.Remove(node);
                dropped.Add(node.Value.Hash);
                if (queued)
                    queue = queue.Dequeue(node.Value.Size);
            }

            node = next;
        }
    }

    /// <summary>One thing for the write task: a control message, or (when <see cref="Message"/> is null) an object to send.</summary>
    /// <param name="Message">The control message, or <see langword="null"/>.</param>
    /// <param name="Hash">The object to send.</param>
    /// <param name="Size">Its length in bytes.</param>
    internal readonly record struct OutboxItem(MeshMessage? Message, ObjectHash Hash, int Size);
}
