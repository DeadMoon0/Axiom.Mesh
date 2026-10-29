using System;
using System.Threading;

namespace Axiom.Mesh.Discovery;

/// <summary>How an <see cref="MdnsDiscovery"/> runs.</summary>
public sealed record MdnsDiscoveryOptions
{
    /// <summary>How often a node announces itself unasked, 60 seconds; the A and SRV records live 120 seconds in a cache.</summary>
    public static readonly TimeSpan DefaultAnnounceInterval = TimeSpan.FromSeconds(60);

    /// <summary>What this node announces.</summary>
    public required SelfRecord Self { get; init; }

    /// <summary>The clock for the announcement gate, the announce interval and the facts.</summary>
    public required TimeProvider Time { get; init; }

    /// <summary>Where what could not be done is recorded.</summary>
    public required DiscoveryFacts Facts { get; init; }

    /// <summary>
    /// How often the node announces itself unasked (and joins interfaces that came up since); it also sends one query
    /// when it opens. <see cref="Timeout.InfiniteTimeSpan"/> announces only when asked and sends no query.
    /// </summary>
    public TimeSpan AnnounceInterval { get; init; } = DefaultAnnounceInterval;
}
