using System;
using System.Net;
using Axiom.LWSE.Connections;
using Axiom.LWSE.Observation;

namespace Axiom.Mesh.Core;

/// <summary>
/// Turns the server's connection events into the node's facts: admitted and refused connections, failed handshakes,
/// closes and handler faults. What a session itself learns (a <c>Hello</c>, a refusal, a duplicate) it records itself.
/// </summary>
internal sealed class NodeObserver(NodeFacts facts) : IServerObserver
{
    /// <inheritdoc />
    public void OnConnectionAccepted(ConnectionContext connection) =>
        facts.Record(NodeFactKind.Accepted, connection.Id, connection.Remote, default, string.Empty);

    /// <inheritdoc />
    public void OnConnectionRejected(IPEndPoint remote, ConnectionRejection reason) =>
        facts.Record(NodeFactKind.Rejected, 0, remote, default, reason.ToString());

    /// <inheritdoc />
    public void OnTransportFailed(ConnectionContext connection, string transport, Exception exception) =>
        facts.Record(NodeFactKind.HandshakeFailed, connection.Id, connection.Remote, default, $"{transport}: {exception.GetType().Name}: {exception.Message}");

    /// <inheritdoc />
    public void OnHandlerFaulted(ConnectionContext connection, Exception exception) =>
        facts.Record(NodeFactKind.HandlerFaulted, connection.Id, connection.Remote, DeviceOf(connection), exception.ToString());

    /// <inheritdoc />
    public void OnConnectionClosed(ConnectionContext connection, TimeSpan duration) =>
        facts.Record(NodeFactKind.Closed, connection.Id, connection.Remote, DeviceOf(connection), $"after {duration.TotalSeconds:0.###} s");

    private static DeviceId DeviceOf(ConnectionContext connection) =>
        connection.TryGetFeature(out PeerIdentity? peer) ? peer.Device : default;
}
