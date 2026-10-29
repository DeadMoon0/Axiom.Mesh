using System;
using System.Net;
using System.Threading;

namespace Axiom.Mesh.Discovery;

/// <summary>How a <see cref="BroadcastDiscovery"/> runs.</summary>
public sealed record BroadcastDiscoveryOptions
{
    /// <summary>How often a node announces itself unasked, 30 seconds.</summary>
    public static readonly TimeSpan DefaultAnnounceInterval = TimeSpan.FromSeconds(30);

    /// <summary>What this node announces.</summary>
    public required SelfRecord Self { get; init; }

    /// <summary>The clock for the announcement gate, the announce interval and the facts.</summary>
    public required TimeProvider Time { get; init; }

    /// <summary>Where what could not be done is recorded.</summary>
    public required DiscoveryFacts Facts { get; init; }

    /// <summary>
    /// The address to bind: <see cref="IPAddress.Any"/> (the default) opens one IPv4 socket on 0.0.0.0 and one IPv6
    /// socket on [::]; any other address opens one socket of its family on that address only.
    /// </summary>
    public IPAddress BindAddress { get; init; } = IPAddress.Any;

    /// <summary>The UDP port to bind and to broadcast to, <see cref="BroadcastCodec.Port"/> by default; 0 binds an ephemeral port (tests).</summary>
    public int Port { get; init; } = BroadcastCodec.Port;

    /// <summary>
    /// How often the node announces itself unasked; it also sends one query when it opens.
    /// <see cref="Timeout.InfiniteTimeSpan"/> announces only when asked (<see cref="IDiscovery.AnnounceAsync"/>) and sends no query.
    /// </summary>
    public TimeSpan AnnounceInterval { get; init; } = DefaultAnnounceInterval;
}
