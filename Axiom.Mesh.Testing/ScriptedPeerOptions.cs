using System.Collections.Generic;
using System.Net;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Testing;

/// <summary>
/// How a <see cref="ScriptedPeer"/> connects: from where and under which ALPN. It always presents its device certificate:
/// on Windows an in-process TLS client that is given none presents one an earlier client of the process used, so a
/// certificate-less peer cannot be scripted faithfully.
/// </summary>
public sealed record ScriptedPeerOptions
{
    /// <summary>A peer that behaves like a node: from 10.0.0.200, ALPN <c>axiom-mesh/1</c>.</summary>
    public static ScriptedPeerOptions Default { get; } = new();

    /// <summary>The address the node sees the peer at. Default <c>10.0.0.200:50000</c>.</summary>
    public IPEndPoint From { get; init; } = new(new IPAddress([10, 0, 0, 200]), 50000);

    /// <summary>The ALPNs the peer offers; empty offers none. Default <c>axiom-mesh/1</c>.</summary>
    public IReadOnlyList<string> Alpn { get; init; } = [Amp.Alpn];
}
