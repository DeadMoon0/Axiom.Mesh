using System;
using System.Diagnostics.Metrics;
using System.Threading;

namespace Axiom.Mesh.Core;

/// <summary>
/// The node's counters (plan §3.7): objects received and sent, Entries stored, violations, as instruments of a
/// <see cref="Meter"/> named <c>Axiom.Mesh</c> (<c>axiom.mesh.objects.in</c>, <c>axiom.mesh.objects.out</c>,
/// <c>axiom.mesh.entries.stored</c>, <c>axiom.mesh.violations</c>) and as running totals the node reports itself. The
/// telemetry series of LOCAL-SPEC.md §10 will read the totals (plan decision D7).
/// </summary>
internal sealed class TelemetrySink : IDisposable
{
    /// <summary>The meter's name.</summary>
    public const string MeterName = "Axiom.Mesh";

    private readonly Meter meter = new(MeterName);
    private readonly Counter<long> objectsIn;
    private readonly Counter<long> objectsOut;
    private readonly Counter<long> entriesStored;
    private readonly Counter<long> violations;
    private long objectsInTotal;
    private long objectsOutTotal;
    private long entriesStoredTotal;
    private long violationsTotal;

    /// <summary>The counters of one node.</summary>
    public TelemetrySink()
    {
        objectsIn = meter.CreateCounter<long>("axiom.mesh.objects.in", "{object}", "Objects received from peers.");
        objectsOut = meter.CreateCounter<long>("axiom.mesh.objects.out", "{object}", "Objects sent to peers in answer to Want.");
        entriesStored = meter.CreateCounter<long>("axiom.mesh.entries.stored", "{entry}", "Entries stored, from peers or written here.");
        violations = meter.CreateCounter<long>("axiom.mesh.violations", "{violation}", "Protocol violations of peers (SPEC.md §12.1).");
    }

    /// <summary>The meter, for listeners.</summary>
    public Meter Meter => meter;

    /// <summary>Objects received so far.</summary>
    public long ObjectsIn => Interlocked.Read(ref objectsInTotal);

    /// <summary>Objects sent so far.</summary>
    public long ObjectsOut => Interlocked.Read(ref objectsOutTotal);

    /// <summary>Entries stored so far.</summary>
    public long EntriesStored => Interlocked.Read(ref entriesStoredTotal);

    /// <summary>Violations counted so far.</summary>
    public long Violations => Interlocked.Read(ref violationsTotal);

    /// <summary>An object arrived from a peer.</summary>
    public void ObjectIn()
    {
        Interlocked.Increment(ref objectsInTotal);
        objectsIn.Add(1);
    }

    /// <summary>An object went out to a peer.</summary>
    public void ObjectOut()
    {
        Interlocked.Increment(ref objectsOutTotal);
        objectsOut.Add(1);
    }

    /// <summary>Entries were stored.</summary>
    /// <param name="count">How many.</param>
    public void Stored(int count)
    {
        if (count == 0)
            return;
        Interlocked.Add(ref entriesStoredTotal, count);
        entriesStored.Add(count);
    }

    /// <summary>A peer broke the protocol.</summary>
    public void Violation()
    {
        Interlocked.Increment(ref violationsTotal);
        violations.Add(1);
    }

    /// <inheritdoc />
    public void Dispose() => meter.Dispose();
}
