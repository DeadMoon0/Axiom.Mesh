using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LWSE.Connections;
using Axiom.LWSE.Protocols;
using Axiom.Mesh.Protocol;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Errors;

namespace Axiom.Mesh.Core;

/// <summary>
/// Serves one AMP/1 session after the TLS handshake (SPEC.md §6, §7.1): sends this node's <c>Hello</c> at once, checks
/// the peer's with <see cref="HelloRule.Check"/>, applies the duplicate rule through the peer table, sends the Roster
/// follow-up, then keeps the session established until it ends, is asked to close, or the node drains. Refusals and
/// violations are counted against the device (§6.2, §12.1).
/// </summary>
/// <remarks>
/// The replication of §10 is not here yet: an established session reads and drops what arrives, and the definition
/// answers <c>Ping</c> and refuses every <c>ServiceOpen</c> on its own. One instance serves every session of the node.
/// </remarks>
internal sealed class PeerSessionHandler : ISessionHandler<MeshMessage, MeshMessage>
{
    private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(5);

    private readonly Func<TrustSnapshot> trust;
    private readonly PeerTable table;
    private readonly NodeFacts facts;
    private readonly TimeProvider time;

    /// <summary>The handler of a node.</summary>
    /// <param name="trust">The node's trust now.</param>
    /// <param name="table">The node's peer table.</param>
    /// <param name="facts">Where to record what happens.</param>
    /// <param name="time">The node's clock.</param>
    public PeerSessionHandler(Func<TrustSnapshot> trust, PeerTable table, NodeFacts facts, TimeProvider time)
    {
        this.trust = trust;
        this.table = table;
        this.facts = facts;
        this.time = time;
    }

    /// <inheritdoc />
    public ProtocolDefinition<MeshMessage, MeshMessage> Definition => Amp.Definition;

    /// <inheritdoc />
    public async ValueTask ServeAsync(ConnectionContext connection, Session<MeshMessage, MeshMessage> session, CancellationToken stopping)
    {
        // MeshTls sets both features on every connection it lets through. A pairing connection belongs to the pairing
        // shell, which is not built yet: it is closed without an AMP/1 byte (returning would send the definition's Bye).
        if (!connection.TryGetFeature(out PeerIdentity? peer) || !connection.TryGetFeature(out LinkActivity? activity) || peer.IsPairing)
        {
            connection.Abort();
            return;
        }

        TrustSnapshot current = trust();
        if (current.Self is not { } self)
            return;

        using var tracked = new PeerSession(connection.Id, peer, activity, peer.Role == TlsRole.Client ? self.Device : peer.Device, connection.Remote);
        table.SessionOpened(tracked);
        facts.Record(NodeFactKind.SessionOpened, connection.Id, connection.Remote, peer.Device, $"{peer.Protocol} {peer.CipherSuite} as {peer.Role}");
        bool established = false;
        string ended = "closed";
        try
        {
            await session.WriteAsync(OwnHello(current, self), stopping).ConfigureAwait(false);
            ProtocolReadResult<MeshMessage> first = await session.ReadAsync(stopping).ConfigureAwait(false);
            if (first.Message is not Hello hello)
            {
                table.RefusedBeforeHello(tracked, first.Message is Bye bye ? bye.ToString() : "closed without a Hello");
                return;
            }

            current = trust();
            HelloOutcome outcome = HelloRule.Check(hello, new PeerIdentityFacts(peer.Key, HeldRosterSeq(current, peer.User)), current);
            if (outcome is HelloOutcome.Refused refused)
            {
                facts.Record(NodeFactKind.HelloRefused, connection.Id, connection.Remote, peer.Device, refused.Bye.Detail);
                table.Violation(peer.Device, connection.Id, "Hello: " + refused.Bye.Detail);
                await SendByeAsync(session, refused.Bye).ConfigureAwait(false);
                return;
            }

            established = true;
            EstablishVerdict verdict = table.Establish(tracked);
            if (!verdict.KeepThis)
            {
                facts.Record(NodeFactKind.DuplicateClosed, connection.Id, connection.Remote, peer.Device, "an older session to the device is kept");
                await SendByeAsync(session, new Bye(ByeReason.Duplicate, string.Empty)).ConfigureAwait(false);
                return;
            }

            verdict.CloseOther?.RequestClose(ByeReason.Duplicate);
            await FollowUpAsync(session, (HelloOutcome.Ok)outcome, current, peer.User, stopping).ConfigureAwait(false);
            ended = await ServeEstablishedAsync(session, tracked, stopping).ConfigureAwait(false);
        }
        catch (ProtocolViolationException violation)
        {
            ended = Describe(violation.Violation);
            if (!established)
                table.RefusedBeforeHello(tracked, Describe(violation.Violation));
            if (violation.Violation.Code != ViolationCode.Timeout)
                table.Violation(peer.Device, connection.Id, Describe(violation.Violation));
        }
        catch (Exception ex) when (ex is ProtoStreamException or IOException or ObjectDisposedException || (ex is OperationCanceledException && stopping.IsCancellationRequested))
        {
            // The transport ended underneath, or the node drained before the peer's Hello.
            ended = ex.GetType().Name + ": " + ex.Message + (ex.InnerException is { } inner ? " (" + inner.GetType().Name + ": " + inner.Message + ")" : string.Empty);
            if (!established && !stopping.IsCancellationRequested)
                table.RefusedBeforeHello(tracked, ex.Message);
        }
        finally
        {
            table.SessionEnded(tracked, ended);
        }
    }

    /// <summary>This node's <c>Hello</c> (SPEC.md §7.1): its device and user, the seq of its own Roster as it holds it.</summary>
    /// <param name="trust">The trust the Roster seq is read from.</param>
    /// <param name="self">This node's identity.</param>
    /// <returns>The message.</returns>
    public static Hello OwnHello(TrustSnapshot trust, SelfIdentity self) =>
        new(self.Device, self.User, HeldRosterSeq(trust, self.User), Capabilities.None, []);

    private static string Describe(Violation violation) => $"{violation.Code} in {violation.State}: {violation.Detail}";

    private static ulong HeldRosterSeq(TrustSnapshot trust, UserId user) =>
        trust.TryGetUser(user, out UserTrust? held) && held.Roster is { } roster ? roster.Content.Seq : 0;

    // SPEC.md §7.1: the peer's user's Roster, when the peer holds an older one; a query, when it holds a newer one.
    private static async ValueTask FollowUpAsync(Session<MeshMessage, MeshMessage> session, HelloOutcome.Ok ok, TrustSnapshot trust, UserId user, CancellationToken stopping)
    {
        if (ok.FollowUp == RosterFollowUp.SendHeldRoster && trust.TryGetUser(user, out UserTrust? held) && held.Roster is { } roster)
            await session.WriteAsync(ObjectMessage.From(roster), stopping).ConfigureAwait(false);
        else if (ok.FollowUp == RosterFollowUp.QueryRoster)
            await session.WriteAsync(new RosterQuery(user), stopping).ConfigureAwait(false);
    }

    private async ValueTask<string> ServeEstablishedAsync(Session<MeshMessage, MeshMessage> session, PeerSession tracked, CancellationToken stopping)
    {
        using var ending = CancellationTokenSource.CreateLinkedTokenSource(stopping, tracked.Closing);
        while (true)
        {
            ProtocolReadResult<MeshMessage> read;
            try
            {
                read = await session.ReadAsync(ending.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ending.IsCancellationRequested)
            {
                return await CloseAsync(session, tracked, stopping.IsCancellationRequested).ConfigureAwait(false);
            }

            if (read.IsCompleted)
                return session.State == Amp.Closed ? "the peer said Bye" : "the peer closed";

            // Replication (SPEC.md §10) handles what arrives here; until it does, an established session only lives.
        }
    }

    private async ValueTask<string> CloseAsync(Session<MeshMessage, MeshMessage> session, PeerSession tracked, bool draining)
    {
        ByeReason reason = draining ? ByeReason.Shutdown : tracked.CloseReason;
        if (reason == ByeReason.Shutdown)
        {
            using var wait = new CancellationTokenSource(CloseWait, time);
            await session.CloseAsync(wait.Token).ConfigureAwait(false);
            return "closed with Bye(Shutdown)";
        }

        NodeFactKind kind = reason == ByeReason.Duplicate ? NodeFactKind.DuplicateClosed : NodeFactKind.Unauthorized;
        facts.Record(kind, tracked.Connection, tracked.Remote, tracked.Device, "closed with Bye(" + reason + ")");
        await SendByeAsync(session, new Bye(reason, string.Empty)).ConfigureAwait(false);
        return "closed with Bye(" + reason + ")";
    }

    private async ValueTask SendByeAsync(Session<MeshMessage, MeshMessage> session, Bye bye)
    {
        using var wait = new CancellationTokenSource(CloseWait, time);
        try
        {
            await session.WriteAsync(bye, wait.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ProtoStreamException or IOException or OperationCanceledException)
        {
            // The peer is gone or does not read; the connection closes either way.
        }
    }
}
