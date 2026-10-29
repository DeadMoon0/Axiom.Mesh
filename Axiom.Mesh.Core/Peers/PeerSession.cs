using System;
using System.Net;
using System.Threading;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Core;

/// <summary>
/// One AMP/1 session to a peer device, as the peer table tracks it: who dialled it, when it last received a byte, and
/// a way to ask its handler to close it with a <c>Bye</c> of a given reason (the duplicate rule, a removal).
/// </summary>
internal sealed class PeerSession : IDisposable
{
    private readonly CancellationTokenSource closing = new();
    private int closeReason = -1;

    /// <summary>A session that has just opened.</summary>
    /// <param name="connection">The connection's number in the node's server.</param>
    /// <param name="peer">What the handshake proved.</param>
    /// <param name="activity">When the connection last received a byte.</param>
    /// <param name="dialledBy">The device that dialled the connection.</param>
    /// <param name="remote">The peer's address.</param>
    public PeerSession(long connection, PeerIdentity peer, LinkActivity activity, DeviceId dialledBy, IPEndPoint remote)
    {
        Connection = connection;
        Peer = peer;
        Activity = activity;
        DialledBy = dialledBy;
        Remote = remote;
    }

    /// <summary>The connection's number.</summary>
    public long Connection { get; }

    /// <summary>What the handshake proved about the peer.</summary>
    public PeerIdentity Peer { get; }

    /// <summary>The peer's device.</summary>
    public DeviceId Device => Peer.Device;

    /// <summary>When the connection last received a byte.</summary>
    public LinkActivity Activity { get; }

    /// <summary>The device that dialled the connection (SPEC.md §4.5).</summary>
    public DeviceId DialledBy { get; }

    /// <summary>The peer's address.</summary>
    public IPEndPoint Remote { get; }

    /// <summary>Whether this node dialled the connection.</summary>
    public bool IsDialledHere => Peer.Role == TlsRole.Client;

    /// <summary>Cancelled when the session is asked to close.</summary>
    public CancellationToken Closing => closing.Token;

    /// <summary>Why it was asked to close; <see cref="ByeReason.Shutdown"/> when it was not.</summary>
    public ByeReason CloseReason
    {
        get
        {
            int reason = Volatile.Read(ref closeReason);
            return reason < 0 ? ByeReason.Shutdown : (ByeReason)reason;
        }
    }

    /// <summary>Asks the handler to close with <c>Bye(<paramref name="reason"/>)</c>; the first reason asked wins.</summary>
    /// <param name="reason">Why.</param>
    public void RequestClose(ByeReason reason)
    {
        if (Interlocked.CompareExchange(ref closeReason, (int)reason, -1) != -1)
            return;
        try
        {
            closing.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The session ended meanwhile; nothing left to close.
        }
    }

    /// <inheritdoc />
    public void Dispose() => closing.Dispose();

    /// <inheritdoc />
    public override string ToString() => $"#{Connection} {Device} dialled by {(IsDialledHere ? "this node" : "the peer")}";
}
