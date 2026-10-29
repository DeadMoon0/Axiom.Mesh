using Axiom.LWSE.Adoption;
using Axiom.LWSE.Protocols;
using Axiom.Mesh.Local;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Core;

/// <summary>
/// The three ports of a node's one server (DESIGN.md §10.2) and the protocols they speak: the mesh port (bound, or a
/// number in memory) and two adopt-only ports, one for the connections the node dials and one for local clients.
/// </summary>
internal static class Ports
{
    /// <summary>Connections this node dialled: <c>MeshTls</c> as client, then AMP/1.</summary>
    public static AdoptOnlyPort Outbound { get; } = new("outbound");

    /// <summary>Pipe connections and in-process services: <c>axiom-mesh-local/1</c>, no TLS.</summary>
    public static AdoptOnlyPort Local { get; } = new("local");

    /// <summary>AMP/1, the entry protocol of the mesh and outbound ports.</summary>
    public static ProtocolKey<MeshMessage, MeshMessage, NoPortOptions> MeshProtocolKey { get; } = new(Amp.Alpn, Amp.Alpn);

    /// <summary>The local protocol, the entry protocol of the local port.</summary>
    public static ProtocolKey<IClientMessage, IHostMessage, NoPortOptions> LocalProtocolKey { get; } = new(Axiom.Mesh.Local.LocalProtocol.Name);
}
