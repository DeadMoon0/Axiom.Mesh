using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Sync;

/// <summary>
/// Reads the events of <c>axiom-mesh-sync/1</c> from topic <c>service</c> (LOCAL-SPEC.md §7.3 <c>ServiceEvent</c>,
/// SYNC-SERVICE.md §6). An app subscribes with <see cref="Filter"/> and hands every event here. The service emits its
/// events with a zero Package, so one subscription sees every binding.
/// </summary>
public static class SyncEventReader
{
    /// <summary>The filter that subscribes to every event of the sync service.</summary>
    public static Filter Filter { get; } = new() { Service = SyncProtocol.ServiceName };

    /// <summary>The event a <c>ServiceEvent</c> carries, when it is one of the sync service's that this library knows.</summary>
    /// <param name="received">The event as the Host delivered it.</param>
    /// <param name="sync">The sync event, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="false"/> for another service's event, an event name this library does not know (a later version's;
    /// ignore it) or a payload that does not parse.
    /// </returns>
    public static bool TryRead(ServiceEvent received, [NotNullWhen(true)] out SyncEvent? sync)
    {
        ArgumentNullException.ThrowIfNull(received);
        sync = null;
        if (received.Service != SyncProtocol.ServiceName)
            return false;
        sync = received.Event switch
        {
            SyncEvents.Binding => SyncCodec.TryDecode(received.Payload, out BindingEvent? binding) ? binding : null,
            SyncEvents.Conflict => SyncCodec.TryDecode(received.Payload, out ConflictEvent? conflict) ? conflict : null,
            _ => null,
        };
        return sync is not null;
    }
}
