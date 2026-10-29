using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;

namespace Axiom.Mesh.Core;

/// <summary>
/// Everything replication shares on one node (SPEC.md §10): the store writer and store, intake, the want scheduler, the
/// replicator, the counters, the facts and the peer table, the clock, and the established sessions, which it reports in
/// <see cref="Status"/>. Sessions take what they need from it; it owns nothing a session owns.
/// </summary>
internal sealed class NodeReplication : IDisposable
{
    private readonly ConcurrentDictionary<long, ReplicationSession> sessions = new();

    /// <summary>The replication of a node.</summary>
    /// <param name="writer">The store writer.</param>
    /// <param name="bus">Its change bus.</param>
    /// <param name="store">The object store.</param>
    /// <param name="table">The peer table.</param>
    /// <param name="facts">The node's facts.</param>
    /// <param name="time">The node's clock.</param>
    public NodeReplication(StoreWriter writer, ChangeBus bus, ObjectStore store, PeerTable table, NodeFacts facts, TimeProvider time)
    {
        Writer = writer;
        Bus = bus;
        Store = store;
        Table = table;
        Facts = facts;
        Time = time;
        Wants = new WantScheduler(time);
        Intake = new Intake(writer, Wants);
        Telemetry = new TelemetrySink();
        Replicator = new Replicator(writer, bus, Intake, Wants, facts, Telemetry, time);
    }

    /// <summary>The store writer.</summary>
    public StoreWriter Writer { get; }

    /// <summary>The change bus every session subscribes to once.</summary>
    public ChangeBus Bus { get; }

    /// <summary>The object store answers are read from.</summary>
    public ObjectStore Store { get; }

    /// <summary>The peer table violations and faulty content are counted in.</summary>
    public PeerTable Table { get; }

    /// <summary>The node's facts.</summary>
    public NodeFacts Facts { get; }

    /// <summary>The node's clock.</summary>
    public TimeProvider Time { get; }

    /// <summary>Every object the node wants, and from whom.</summary>
    public WantScheduler Wants { get; }

    /// <summary>Where objects from peers enter.</summary>
    public Intake Intake { get; }

    /// <summary>The node's counters.</summary>
    public TelemetrySink Telemetry { get; }

    /// <summary>The loop that follows the store: history walk, held buffer, cancellations.</summary>
    public Replicator Replicator { get; }

    /// <summary>Buffers objects are read into on their way out: up to the largest object, a few per size.</summary>
    public ArrayPool<byte> SendPool { get; } = PooledCopy.CreatePool();

    /// <summary>A session became established.</summary>
    /// <param name="session">The session.</param>
    public void Opened(ReplicationSession session) => sessions[session.Connection] = session;

    /// <summary>A session ended.</summary>
    /// <param name="session">The session.</param>
    public void Closed(ReplicationSession session) => sessions.TryRemove(session.Connection, out _);

    /// <summary>Where replication stands now.</summary>
    /// <returns>A snapshot.</returns>
    public ReplicationStatus Status()
    {
        SessionTraffic[] traffic = [.. sessions.Values.Select(s => s.Traffic()).OrderBy(t => t.Device)];
        bool idle = Writer.Pending == 0 && Replicator.IsIdle && Wants.IsIdle && traffic.All(t => t.IsIdle);
        return new ReplicationStatus
        {
            WantsOutstanding = Wants.Outstanding,
            WantsWaiting = Wants.Waiting,
            ObjectsIn = Telemetry.ObjectsIn,
            ObjectsOut = Telemetry.ObjectsOut,
            EntriesStored = Telemetry.EntriesStored,
            Violations = Telemetry.Violations,
            HeldBytes = Intake.Held.Bytes,
            Sessions = traffic,
            IsIdle = idle && Writer.Pending == 0,
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Replicator.Dispose();
        Wants.Dispose();
        Telemetry.Dispose();
    }
}
