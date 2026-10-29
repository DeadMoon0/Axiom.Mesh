using System;
using System.Collections.Generic;
using System.Net;
using Axiom.Mesh.Discovery;

namespace Axiom.Mesh.Core;

/// <summary>One peer device as the node's peer table holds it at one moment: an immutable view.</summary>
public sealed class PeerRecord
{
    /// <summary>The peer's device id.</summary>
    public required DeviceId Device { get; init; }

    /// <summary>The peer's user as last claimed or proven; zero when not known.</summary>
    public required UserId User { get; init; }

    /// <summary>Where the peer stands now.</summary>
    public required PeerState State { get; init; }

    /// <summary>The addresses it was heard at or dialled on, most recent first.</summary>
    public required IReadOnlyList<IPEndPoint> Addresses { get; init; }

    /// <summary>The discovery mechanisms that reported it.</summary>
    public required DiscoverySource Sources { get; init; }

    /// <summary>When it was last heard of or connected; <see langword="null"/> when never.</summary>
    public DateTimeOffset? LastSeen { get; init; }

    /// <summary>When its backoff ends; <see langword="null"/> when there is none.</summary>
    public DateTimeOffset? BackoffUntil { get; init; }

    /// <summary>Failed dials and refusals in a row since the last established session.</summary>
    public required int Failures { get; init; }

    /// <summary>Violations within the current hour (SPEC.md §12.1).</summary>
    public required int Violations { get; init; }

    /// <summary>Sessions open to it now, established or awaiting the peer's <c>Hello</c>.</summary>
    public required int Sessions { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{Device} {State} sessions {Sessions} failures {Failures} violations {Violations}";
}
