using System.Threading;
using System.Threading.Tasks;
using Axiom.LWSE.Connections;
using Axiom.LWSE.Protocols;
using Axiom.Mesh.Local;
using Axiom.ProtoStream;

namespace Axiom.Mesh.Core;

/// <summary>
/// The local port's protocol, <c>axiom-mesh-local/1</c> (LOCAL-SPEC.md), as a placeholder: the port exists so that
/// pipe connections and in-process services have their place in the server's admission, observers and drain, but the
/// Host side of the protocol is not served yet, so every connection is closed at once.
/// </summary>
internal sealed class LocalPortHandler : ISessionHandler<IClientMessage, IHostMessage>
{
    /// <inheritdoc />
    public ProtocolDefinition<IClientMessage, IHostMessage> Definition => LocalProtocol.HostSide;

    /// <inheritdoc />
    public ValueTask ServeAsync(ConnectionContext connection, Session<IClientMessage, IHostMessage> session, CancellationToken stopping) =>
        ValueTask.CompletedTask;
}
