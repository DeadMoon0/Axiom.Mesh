using System;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Linq;

namespace Axiom.Mesh.Benchmarks;

/// <summary>
/// Samples the runtime's allocation ticks in this process (one event per ~100 KB allocated, with the type that crossed
/// the threshold), so a scenario can say which types its allocations are; enabled with <c>AXIOM_MESH_ALLOCATIONS=1</c>.
/// </summary>
internal sealed class AllocationListener : EventListener
{
    private const int AllocationTick = 10;
    private readonly ConcurrentDictionary<string, long> bytesByType = new(StringComparer.Ordinal);

    /// <summary>Whether the variable asks for the sampling.</summary>
    public static bool Requested => Environment.GetEnvironmentVariable("AXIOM_MESH_ALLOCATIONS") == "1";

    /// <summary>The types that allocated most, with their sampled bytes.</summary>
    /// <param name="top">How many.</param>
    /// <returns>One line.</returns>
    public string Top(int top) =>
        string.Join(", ", bytesByType.OrderByDescending(p => p.Value).Take(top).Select(p => $"{p.Key} {p.Value / (1 << 20)} MiB"));

    /// <inheritdoc />
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == "Microsoft-Windows-DotNETRuntime")
            EnableEvents(eventSource, EventLevel.Verbose, (EventKeywords)0x1);
    }

    /// <inheritdoc />
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventId != AllocationTick || eventData.Payload is null || eventData.PayloadNames is null)
            return;
        int type = eventData.PayloadNames.IndexOf("TypeName");
        int amount = eventData.PayloadNames.IndexOf("AllocationAmount64");
        if (type < 0 || amount < 0)
            return;
        bytesByType.AddOrUpdate(eventData.Payload[type] as string ?? "?", Convert.ToInt64(eventData.Payload[amount]), (_, sum) => sum + Convert.ToInt64(eventData.Payload[amount]));
    }
}
