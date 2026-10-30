using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The callbacks the Host has outstanding toward one service (LOCAL-SPEC.md §3.6, §9.8): ids chosen here, never 0, never
/// reused on the connection; at most 64 outstanding (<c>Busy</c> past that); each with its deadline, after which it leaves
/// the outstanding set and its caller hears <c>ServiceTimeout</c>. A <c>Reply</c> completes its callback; a <c>Reply</c> for
/// an id that expired within the last 5 minutes is ignored; one for an id never issued is a violation.
/// </summary>
internal sealed class CallbackTable : IDisposable
{
    private static readonly TimeSpan LateWindow = TimeSpan.FromMinutes(5);

    private readonly object gate = new();
    private readonly TimeProvider time;
    private readonly Dictionary<uint, Pending> outstanding = [];
    private readonly Dictionary<uint, long> expired = [];
    private uint lastId;
    private bool closed;

    /// <summary>The table of one connection.</summary>
    /// <param name="time">The node's clock: deadlines and the 5-minute window run on it.</param>
    public CallbackTable(TimeProvider time) => this.time = time ?? throw new ArgumentNullException(nameof(time));

    /// <summary>Callbacks outstanding now.</summary>
    public int Outstanding
    {
        get
        {
            lock (gate)
                return outstanding.Count;
        }
    }

    /// <summary>Issues a callback: an id, and the task its <c>Reply</c> completes.</summary>
    /// <param name="deadline">How long the service has to reply (§9.8: 60 seconds for <c>ForwardedCall</c>).</param>
    /// <param name="id">The callback id to send it with.</param>
    /// <param name="reply">Completes with the <c>Reply</c>; with <see langword="null"/> at the deadline or when the connection ends.</param>
    /// <returns><see langword="false"/> when 64 are outstanding (<c>Busy</c>) or the connection ended.</returns>
    public bool TryIssue(TimeSpan deadline, out uint id, out Task<Reply?> reply)
    {
        lock (gate)
        {
            if (closed || outstanding.Count >= Limits.MaxOutstandingCallbacks)
            {
                id = 0;
                reply = Task.FromResult<Reply?>(null);
                return false;
            }

            id = ++lastId;
            var pending = new Pending(id);
            pending.Timer = time.CreateTimer(static state => ((Expiry)state!).Fire(), new Expiry(this, id), deadline, Timeout.InfiniteTimeSpan);
            outstanding.Add(id, pending);
            reply = pending.Completion.Task;
            return true;
        }
    }

    /// <summary>A <c>Reply</c> arrived (§3.6).</summary>
    /// <param name="reply">The reply.</param>
    /// <returns><see langword="false"/> for a callback id this connection never issued: a violation.</returns>
    public bool Route(Reply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        Pending? pending;
        lock (gate)
        {
            Prune();
            if (!outstanding.Remove(reply.CallbackId, out pending))
                return expired.ContainsKey(reply.CallbackId);
        }

        pending.Timer?.Dispose();
        pending.Completion.TrySetResult(reply);
        return true;
    }

    /// <summary>The connection ended: every outstanding callback completes without a reply.</summary>
    public void Dispose()
    {
        List<Pending> ending;
        lock (gate)
        {
            closed = true;
            ending = [.. outstanding.Values];
            outstanding.Clear();
        }

        foreach (Pending pending in ending)
        {
            pending.Timer?.Dispose();
            pending.Completion.TrySetResult(null);
        }
    }

    private void Expire(uint id)
    {
        Pending? pending;
        lock (gate)
        {
            if (!outstanding.Remove(id, out pending))
                return;
            expired[id] = time.GetTimestamp();
        }

        pending.Timer?.Dispose();
        pending.Completion.TrySetResult(null);
    }

    // Ids that expired more than 5 minutes ago are never issued again, so a Reply naming one is a violation once forgotten.
    private void Prune()
    {
        List<uint>? old = null;
        foreach ((uint id, long at) in expired)
        {
            if (time.GetElapsedTime(at) > LateWindow)
                (old ??= []).Add(id);
        }

        foreach (uint id in old ?? [])
            expired.Remove(id);
    }

    private sealed class Pending(uint id)
    {
        public uint Id { get; } = id;

        public TaskCompletionSource<Reply?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ITimer? Timer { get; set; }
    }

    private sealed class Expiry(CallbackTable table, uint id)
    {
        public void Fire() => table.Expire(id);
    }
}
