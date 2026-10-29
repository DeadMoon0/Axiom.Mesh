using System.Threading;
using System.Threading.Tasks;
using Axiom.LWSE.Connections;
using Axiom.LWSE.Protocols;
using Axiom.Mesh.Local;
using Axiom.ProtoStream;

namespace Axiom.Mesh.Core;

/// <summary>
/// The local port's protocol handler (LOCAL-SPEC.md): LWSE hands it every connection the port adopted, and it serves each
/// as a <see cref="LocalConnection"/> with the process identity the port recorded at adoption. A connection that did
/// not come through <see cref="LocalPort.AdoptAsync"/> carries no identity and is closed at once.
/// </summary>
internal sealed class LocalPortHandler : ISessionHandler<IClientMessage, IHostMessage>
{
    private readonly LocalPort port;

    /// <summary>The handler of a port.</summary>
    /// <param name="port">The port.</param>
    public LocalPortHandler(LocalPort port) => this.port = port;

    /// <inheritdoc />
    public ProtocolDefinition<IClientMessage, IHostMessage> Definition => LocalProtocol.HostSide;

    /// <inheritdoc />
    public async ValueTask ServeAsync(ConnectionContext connection, Session<IClientMessage, IHostMessage> session, CancellationToken stopping)
    {
        if (!port.TryTakeArrival(connection.Remote, out LocalArrival? arrival))
            return;

        var local = new LocalConnection(port, connection, session, arrival.Identity, arrival.Activity);
        port.Opened(local);
        try
        {
            // The server drains after the port's own Drain: whatever is still open says Bye(Shutdown) now (§4.5).
            using CancellationTokenRegistration shutdown = stopping.Register(static state => ((LocalConnection)state!).Shutdown(), local);
            await local.ServeAsync().ConfigureAwait(false);
        }
        finally
        {
            port.Closed(local);
        }
    }
}
