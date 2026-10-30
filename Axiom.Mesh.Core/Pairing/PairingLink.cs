using System;
using System.Net;
using System.Threading;
using Axiom.Mesh.Protocol;
using Axiom.ProtoStream;

namespace Axiom.Mesh.Core;

/// <summary>
/// One pairing connection (PAIR-SPEC.md §4, §5): the <c>axiom-mesh-pair/1</c> session the connection switched to, the
/// peer its TLS handshake proved, and the means to end it from outside its read loop (a user's command, the expiry).
/// </summary>
internal sealed class PairingLink : IDisposable
{
    private readonly CancellationTokenSource ending = new();

    /// <summary>A link.</summary>
    /// <param name="session">The pairing session.</param>
    /// <param name="peer">What the handshake proved about the other side.</param>
    /// <param name="connection">The connection's id, for the facts.</param>
    /// <param name="remote">The other side's address.</param>
    public PairingLink(Session<PairMessage, PairMessage> session, PeerIdentity peer, long connection, IPEndPoint remote)
    {
        Session = session;
        Peer = peer;
        Connection = connection;
        Remote = remote;
    }

    /// <summary>The pairing session.</summary>
    public Session<PairMessage, PairMessage> Session { get; }

    /// <summary>What the handshake proved: the other side's TLS key, and whether this node dialled.</summary>
    public PeerIdentity Peer { get; }

    /// <summary>Whether this node dialled: the entering side.</summary>
    public bool IsEntering => Peer.Role == TlsRole.Client;

    /// <summary>The connection's id.</summary>
    public long Connection { get; }

    /// <summary>The other side's address.</summary>
    public IPEndPoint Remote { get; }

    /// <summary>Cancelled when the link is to close: its read loop stops and the connection ends.</summary>
    public CancellationToken Ending => ending.Token;

    /// <summary>Asks the read loop to stop and the connection to close.</summary>
    public void End()
    {
        try
        {
            ending.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The link ended meanwhile.
        }
    }

    /// <inheritdoc />
    public void Dispose() => ending.Dispose();
}
