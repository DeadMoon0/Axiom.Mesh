using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace Axiom.Mesh.Discovery;

/// <summary>The addresses a user configured (SPEC.md §3.3), and how often they are offered to the dialler.</summary>
public sealed record StaticPeersOptions
{
    /// <summary>How often the list is offered again, 60 seconds.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

    /// <summary>The configured peers: an <see cref="IPEndPoint"/>, or a <see cref="DnsEndPoint"/> resolved on every round.</summary>
    public required IReadOnlyList<EndPoint> Peers { get; init; }

    /// <summary>The clock for the interval and the facts.</summary>
    public required TimeProvider Time { get; init; }

    /// <summary>Where a name that did not resolve is recorded.</summary>
    public required DiscoveryFacts Facts { get; init; }

    /// <summary>How often the list is offered again; <see cref="Timeout.InfiniteTimeSpan"/> offers it once per observer.</summary>
    public TimeSpan Interval { get; init; } = DefaultInterval;
}
