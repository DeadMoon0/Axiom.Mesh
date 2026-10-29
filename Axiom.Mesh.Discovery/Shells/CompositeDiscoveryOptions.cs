using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Discovery;

/// <summary>What a <see cref="CompositeDiscovery"/> merges, and the cache that decides what is new.</summary>
public sealed record CompositeDiscoveryOptions
{
    /// <summary>The mechanisms to merge, for instance mDNS, broadcast and the static peers.</summary>
    public required IReadOnlyList<IDiscovery> Parts { get; init; }

    /// <summary>The clock the cache's timestamps come from.</summary>
    public required TimeProvider Time { get; init; }

    /// <summary>The newest claim per device; bounded, so an announcement of an unknown device costs one entry and nothing else.</summary>
    public required AnnouncementCache Cache { get; init; }
}
