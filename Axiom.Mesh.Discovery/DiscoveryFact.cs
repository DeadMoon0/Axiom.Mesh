using System;

namespace Axiom.Mesh.Discovery;

/// <summary>One thing a discovery shell could not do, recorded for the engine to log; the shell carried on without it.</summary>
public sealed record DiscoveryFact
{
    /// <summary>When it happened, from the shell's <see cref="TimeProvider"/>.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>The mechanism that recorded it.</summary>
    public required DiscoverySource Mechanism { get; init; }

    /// <summary>What could not be done.</summary>
    public required DiscoveryFactKind Kind { get; init; }

    /// <summary>The interface, socket or host it concerns, for instance <c>eth0</c>, <c>udp4 0.0.0.0:41811</c> or <c>nas.local</c>.</summary>
    public required string Subject { get; init; }

    /// <summary>The reason as the platform stated it, for instance the socket error.</summary>
    public required string Detail { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{Mechanism} {Kind} {Subject}: {Detail}";
}
