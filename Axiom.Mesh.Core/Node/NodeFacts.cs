using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Core;

/// <summary>
/// The facts a node records about its connections and peers, in order: the newest ones kept in a bounded ring, each
/// logged once, and waited for by whoever needs one (a test, the CLI's watch). Recording never blocks and never
/// throws; the oldest facts drop out of the ring when it is full.
/// </summary>
public sealed class NodeFacts
{
    /// <summary>How many facts the ring keeps by default.</summary>
    public const int DefaultCapacity = 1024;

    private readonly object gate = new();
    private readonly NodeFact[] ring;
    private readonly TimeProvider time;
    private readonly ILogger logger;
    private readonly List<Waiter> waiters = [];
    private long last;
    private int next;
    private int count;

    internal NodeFacts(TimeProvider time, ILogger logger, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        this.time = time;
        this.logger = logger;
        ring = new NodeFact[capacity];
    }

    /// <summary>How many facts the ring keeps.</summary>
    public int Capacity => ring.Length;

    /// <summary>The sequence number of the newest fact; 0 before the first.</summary>
    public long LastSequence
    {
        get
        {
            lock (gate)
                return last;
        }
    }

    /// <summary>The facts the ring still holds, oldest first.</summary>
    /// <returns>A copy.</returns>
    public IReadOnlyList<NodeFact> Recent()
    {
        lock (gate)
        {
            var facts = new NodeFact[count];
            for (int i = 0; i < count; i++)
                facts[i] = ring[(next - count + i + ring.Length) % ring.Length];
            return facts;
        }
    }

    /// <summary>
    /// Waits for the first fact after <paramref name="after"/> that matches: one the ring still holds, or the next one
    /// recorded. Take <see cref="LastSequence"/> before causing what is awaited, so nothing is missed between the two.
    /// </summary>
    /// <param name="match">Which fact.</param>
    /// <param name="after">Only facts with a greater sequence number count.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The fact.</returns>
    public Task<NodeFact> WaitForAsync(Func<NodeFact, bool> match, long after, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);
        var waiter = new Waiter(match, after);
        lock (gate)
        {
            for (int i = 0; i < count; i++)
            {
                NodeFact fact = ring[(next - count + i + ring.Length) % ring.Length];
                if (fact.Sequence > after && match(fact))
                    return Task.FromResult(fact);
            }

            waiters.Add(waiter);
        }

        if (cancellationToken.CanBeCanceled)
        {
            CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                lock (gate)
                    waiters.Remove(waiter);
                waiter.Completion.TrySetCanceled(cancellationToken);
            });
            _ = waiter.Completion.Task.ContinueWith(static (_, state) => ((CancellationTokenRegistration)state!).Dispose(), registration, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        return waiter.Completion.Task;
    }

    /// <summary>Records and logs a fact, and completes every waiter it matches.</summary>
    /// <param name="kind">What happened.</param>
    /// <param name="connection">The connection's number, or 0.</param>
    /// <param name="remote">The peer's address, if known.</param>
    /// <param name="device">The peer's device, or zero.</param>
    /// <param name="detail">More, for logs.</param>
    /// <returns>The fact.</returns>
    internal NodeFact Record(NodeFactKind kind, long connection, IPEndPoint? remote, DeviceId device, string detail) =>
        Record(kind, connection, remote, device, default, detail);

    /// <summary>Records and logs a fact about an object, and completes every waiter it matches.</summary>
    /// <param name="kind">What happened.</param>
    /// <param name="connection">The connection's number, or 0.</param>
    /// <param name="remote">The peer's address, if known.</param>
    /// <param name="device">The peer's device, or zero.</param>
    /// <param name="subject">The object's hash.</param>
    /// <param name="detail">More, for logs.</param>
    /// <returns>The fact.</returns>
    internal NodeFact Record(NodeFactKind kind, long connection, IPEndPoint? remote, DeviceId device, Hash32 subject, string detail)
    {
        List<Waiter>? matched = null;
        NodeFact fact;
        lock (gate)
        {
            fact = new NodeFact { Sequence = ++last, At = time.GetUtcNow(), Kind = kind, Connection = connection, Remote = remote, Device = device, Subject = subject, Detail = detail };
            ring[next] = fact;
            next = (next + 1) % ring.Length;
            count = Math.Min(count + 1, ring.Length);
            for (int i = waiters.Count - 1; i >= 0; i--)
            {
                if (waiters[i].Matches(fact))
                {
                    (matched ??= []).Add(waiters[i]);
                    waiters.RemoveAt(i);
                }
            }
        }

        NodeLog.Fact(logger, LevelOf(kind), fact);
        if (matched is not null)
        {
            foreach (Waiter waiter in matched)
                waiter.Completion.TrySetResult(fact);
        }

        return fact;
    }

    private static LogLevel LevelOf(NodeFactKind kind) => kind switch
    {
        NodeFactKind.HandlerFaulted => LogLevel.Error,
        NodeFactKind.Violation or NodeFactKind.HelloRefused or NodeFactKind.Unauthorized or NodeFactKind.FaultyContent => LogLevel.Warning,
        NodeFactKind.Established or NodeFactKind.SessionEnded or NodeFactKind.BackedOff => LogLevel.Information,
        _ => LogLevel.Debug,
    };

    private sealed class Waiter(Func<NodeFact, bool> match, long after)
    {
        public TaskCompletionSource<NodeFact> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Matches(NodeFact fact)
        {
            try
            {
                return fact.Sequence > after && match(fact);
            }
            catch (Exception ex)
            {
                Completion.TrySetException(ex);
                return true;
            }
        }
    }
}
