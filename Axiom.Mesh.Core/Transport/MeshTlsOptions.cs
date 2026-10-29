using System;

namespace Axiom.Mesh.Core;

/// <summary>What a port states for its <see cref="MeshTlsLayer"/>: its side of the handshake and where it reads what it presents and whom it trusts.</summary>
internal sealed class MeshTlsOptions
{
    /// <summary>The side of the handshake this port runs: server on the mesh port, client on the outbound port.</summary>
    public required TlsRole Role { get; init; }

    /// <summary>The device credential that applies now; <see langword="null"/> while the device has no key (no handshake completes then).</summary>
    public required Func<DeviceCredential?> Credential { get; init; }

    /// <summary>The trust that applies now, read at each handshake: the store writer's current snapshot.</summary>
    public required Func<TrustSnapshot> Trust { get; init; }

    /// <summary>Whether a pairing is under way, read at each handshake.</summary>
    public required PairingGate Pairing { get; init; }

    /// <summary>The node's clock: the handshake deadline and the link's activity run on it.</summary>
    public required TimeProvider Time { get; init; }
}
