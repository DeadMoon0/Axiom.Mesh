using System.Collections.Generic;
using System.Net.Security;
using Axiom.LWSE.Transports;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Core;

/// <summary>The mutual TLS transport of a node (SPEC.md §4.2, §4.3): its key, as ports stack it, and the ALPN sets it offers.</summary>
internal static class MeshTls
{
    /// <summary>The transport's key: the mesh port stacks it as server, the outbound port as client.</summary>
    public static TransportKey<MeshTlsOptions> Key { get; } = new("mesh-tls");

    /// <summary>The name SNI carries: fixed, since peers are pinned by key and never by name (SPEC.md §4.3).</summary>
    public const string TargetHost = "axiom-mesh";

    /// <summary>AMP/1's ALPN alone: what a node offers outside a pairing.</summary>
    /// <returns>A new list, since the TLS stack takes a mutable one.</returns>
    public static List<SslApplicationProtocol> MeshOnly() => [new(Amp.Alpn)];

    /// <summary>AMP/1's and the pairing's ALPN: what the showing side's listener offers while a pairing waits (PAIR-SPEC.md §4.1).</summary>
    /// <returns>A new list.</returns>
    public static List<SslApplicationProtocol> MeshAndPairing() => [new(Amp.Alpn), new(PairProtocol.Alpn)];

    /// <summary>The pairing's ALPN alone: what the entering side offers (PAIR-SPEC.md §4.2).</summary>
    /// <returns>A new list.</returns>
    public static List<SslApplicationProtocol> PairingOnly() => [new(PairProtocol.Alpn)];
}
