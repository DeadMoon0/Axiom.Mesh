using System;
using System.Net;
using Axiom.LWSE.Connections;
using Axiom.LWSE.Modules;
using Axiom.LWSE.Observation;
using Axiom.LWSE.Protocols;
using Axiom.LWSE.Transports;
using Axiom.ProtoStream;

namespace Axiom.Mesh.Spikes;

/// <summary>Registers the spike protocol, the pinned TLS layer and an observer that writes every connection event to the log.</summary>
public sealed class SpikeModule(string node, SpikeLog log) : LWSEModule
{
    public static ProtocolKey<SpikeMessage, SpikeMessage, NoPortOptions> Hello { get; } = new(HelloProtocol.Alpn, HelloProtocol.Alpn);

    public static TransportKey<PinnedTlsOptions> PinnedTls { get; } = new("pinned-tls");

    protected override void Register(ModuleRegistrar registrar)
    {
        registrar.Protocol(Hello, _ => new HelloHandler(node, log));
        registrar.Transport(PinnedTls, (options, _) => new PinnedTlsLayer(options));
        registrar.Observer(new RecordingObserver(node, log));
    }
}

public sealed class RecordingObserver(string node, SpikeLog log) : IServerObserver
{
    public void OnConnectionAccepted(ConnectionContext connection) => log.Add($"[{node}] accepted #{connection.Id} from {connection.Remote}");

    public void OnConnectionRejected(IPEndPoint remote, ConnectionRejection reason) => log.Add($"[{node}] rejected {remote}: {reason}");

    public void OnTransportFailed(ConnectionContext connection, string transport, Exception exception) =>
        log.Add($"[{node}] transport {transport} failed on #{connection.Id}: {exception.GetType().Name}: {exception.Message}");

    public void OnSessionOpened(ConnectionContext connection, ISession session) => log.Add($"[{node}] session opened on #{connection.Id}");

    public void OnHandlerFaulted(ConnectionContext connection, Exception exception) =>
        log.Add($"[{node}] handler faulted on #{connection.Id}: {exception}");

    public void OnConnectionRetired(ConnectionContext connection, RetireReason reason) => log.Add($"[{node}] retired #{connection.Id}: {reason}");

    public void OnConnectionClosed(ConnectionContext connection, TimeSpan duration) => log.Add($"[{node}] closed #{connection.Id}");
}
