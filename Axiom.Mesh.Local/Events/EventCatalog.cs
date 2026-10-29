using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>Reads the body of every event of LOCAL-SPEC.md §7.3 by its message id.</summary>
internal static class EventCatalog
{
    /// <summary>The ids of every event this side knows, ascending.</summary>
    public static ushort[] Ids() => [1024, 1025, 1026, 1027, 1028, 1029, 1030, 1031, 1032, 1033, 1034];

    /// <summary>Reads the body of the event <paramref name="id"/>.</summary>
    /// <returns><see langword="false"/> when the id is unknown or a field is outside its bounds; <paramref name="known"/> tells which.</returns>
    public static bool TryRead(ushort id, ref WireReader reader, out bool known, [NotNullWhen(true)] out LocalEvent? value)
    {
        known = true;
        bool read = id switch
        {
            1024 => Read<PackageChanged>(ref reader, out value),
            1025 => Read<EntryStored>(ref reader, out value),
            1026 => Read<PeerChanged>(ref reader, out value),
            1027 => Read<TransferChanged>(ref reader, out value),
            1028 => Read<OperationChanged>(ref reader, out value),
            1029 => Read<PairingChanged>(ref reader, out value),
            1030 => Read<TelemetrySample>(ref reader, out value),
            1031 => Read<ServiceEvent>(ref reader, out value),
            1032 => Read<UserChanged>(ref reader, out value),
            1033 => Read<StoreChanged>(ref reader, out value),
            1034 => Read<SubscriptionLapsed>(ref reader, out value),
            _ => Unknown(out known, out value),
        };
        return read;
    }

    private static bool Read<T>(ref WireReader reader, [NotNullWhen(true)] out LocalEvent? value)
        where T : LocalEvent, IEvent<T>
    {
        bool read = T.TryRead(ref reader, out T? typed);
        value = typed;
        return read;
    }

    private static bool Unknown(out bool known, out LocalEvent? value)
    {
        known = false;
        value = null;
        return false;
    }
}
