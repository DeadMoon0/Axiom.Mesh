using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LWSE.Connections;
using Axiom.Mesh.Local;
using Axiom.Mesh.Protocol;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Errors;
using LocalMode = Axiom.Mesh.Local.PairingMode;
using MeshMode = Axiom.Mesh.Protocol.PairingMode;
using MeshState = Axiom.Mesh.Protocol.PairingState;

namespace Axiom.Mesh.Core;

/// <summary>
/// The pairing shell (PAIR-SPEC.md; fundamentals plan §2.5): runs one <see cref="PairingMachine"/> at a time over a
/// MeshTls connection under <c>axiom-mesh-pair/1</c>, in all four modes. It opens the <see cref="PairingWindow"/> the TLS
/// layer reads, dials the code on the entering side, exchanges <c>PairHello</c>, derives the short authentication string,
/// takes the users' decisions from the local requests, stores the results once both confirmed, and reports every
/// transition as a <c>PairingChanged</c> event and a fact.
/// </summary>
/// <remarks>
/// <para>
/// Every event (a request, a frame, the expiry, a closed connection) is taken one at a time under one lock, so the machine
/// moves in one order and a result is stored before the frame that announces it is sent. The lock is never held while a
/// frame is awaited.
/// </para>
/// <para>
/// Closing: the showing side closes the connection as soon as the pairing ended, its results stored; the entering side
/// waits for that close before it dials the other side under AMP/1, so the other side has stored its results when the
/// dial arrives.
/// </para>
/// </remarks>
internal sealed class PairingShell : IDisposable
{
    private readonly SemaphoreSlim turn = new(1, 1);
    private readonly PairingGate gate;
    private readonly PeerDialler dialler;
    private readonly PeerTable table;
    private readonly NodeFacts facts;
    private readonly TimeProvider time;
    private readonly string deviceName;
    private readonly Action<Pairing> changed;
    private MeshNode? node;
    private PairingRun? current;
    private uint lastId;
    private int disposed;

    /// <summary>The shell of a node.</summary>
    /// <param name="gate">The window the TLS layer reads.</param>
    /// <param name="dialler">Dials the code's addresses on the entering side.</param>
    /// <param name="table">Dials the other side under AMP/1 after a pairing.</param>
    /// <param name="facts">Where transitions are recorded.</param>
    /// <param name="time">The node's clock: the expiry runs on it.</param>
    /// <param name="deviceName">The name this device proposes for itself when it is enrolled.</param>
    /// <param name="changed">Called with the record after every transition: the <c>PairingChanged</c> event.</param>
    public PairingShell(PairingGate gate, PeerDialler dialler, PeerTable table, NodeFacts facts, TimeProvider time, string deviceName, Action<Pairing> changed)
    {
        this.gate = gate;
        this.dialler = dialler;
        this.table = table;
        this.facts = facts;
        this.time = time;
        this.deviceName = deviceName;
        this.changed = changed;
    }

    private MeshNode Node => node ?? throw new InvalidOperationException("The pairing shell serves only once its node started.");

    /// <summary>Hands the shell its node, once it started.</summary>
    /// <param name="running">The node.</param>
    public void Attach(MeshNode running) => node = running;

    /// <summary>
    /// The mode of a pairing when it is the current one, read without the lock (the dispatcher asks it before a
    /// <c>ConfirmPairing</c> runs, LOCAL-SPEC.md §2.2); <see langword="null"/> for any other pairing.
    /// </summary>
    /// <param name="pairing">The pairing id the request names.</param>
    /// <returns>Its mode, or <see langword="null"/>.</returns>
    public LocalMode? ModeOf(uint pairing) =>
        Volatile.Read(ref current) is { } run && run.Id == pairing && !run.Machine.IsEnded ? (LocalMode)(byte)run.Machine.Mode : null;

    /// <summary>The current or the last pairing (<c>GetPairing</c>); <see langword="null"/> when there was none.</summary>
    /// <returns>The record.</returns>
    public async Task<Pairing?> GetAsync(CancellationToken cancellationToken)
    {
        await turn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return current?.Record();
        }
        finally
        {
            turn.Release();
        }
    }

    /// <summary>
    /// <c>StartPairing</c> (LOCAL-SPEC.md §6.2): checks the mode, starts the machine unless a pairing runs (<c>Busy</c>),
    /// opens the window, and on the entering side starts dialling the code.
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <param name="code">The code; empty for the showing modes.</param>
    /// <param name="cancellationToken">Cancels waiting for the lock and the key store.</param>
    /// <returns>The pairing Waiting, or why it did not start.</returns>
    public async Task<(RequestRefusal? Refusal, Pairing? Pairing)> StartAsync(LocalMode mode, string code, CancellationToken cancellationToken)
    {
        MeshNode running = Node;
        await turn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = time.GetUtcNow();
            bool userKey = mode == LocalMode.AddDevice && await running.HoldsUserKeyNowAsync(cancellationToken).ConfigureAwait(false);
            if (PairingStartRule.Check(mode, code, running.Trust, userKey, out PairingCode? entered) is { } refusal)
                return (refusal, null);
            if (!PairingMachine.TryStart(current?.Machine, (MeshMode)(byte)mode, now, out PairingMachine? machine))
                return (new RequestRefusal(Status.Busy, "A pairing runs already; confirm or cancel it first, or wait until it expires.", string.Empty), null);

            PairingCode? shown = null;
            if (mode is LocalMode.ShowUser or LocalMode.ShowDevice)
            {
                DeviceCredential credential = await running.EnsureDeviceCredentialAsync(cancellationToken).ConfigureAwait(false);
                IReadOnlyList<PairingAddress> addresses = CodeAddresses(running.PairingAddresses(), (MeshMode)(byte)mode, credential.Key);
                if (addresses.Count == 0)
                    return (new RequestRefusal(Status.IoError, "This device has no network address to show in a code; connect it to a network first.", string.Empty), null);
                shown = new PairingCode((MeshMode)(byte)mode, addresses, credential.Key);
            }

            current?.Dispose();
            var run = new PairingRun(++lastId == 0 ? ++lastId : lastId, machine, shown, entered);
            current = run;
            run.Expiry = time.CreateTimer(_ => Expire(run), null, machine.Expires - now, Timeout.InfiniteTimeSpan);
            if (shown is not null)
                gate.Set(PairingWindow.Showing);
            Changed(run);
            if (entered is not null)
                _ = Task.Run(() => EnterAsync(run), CancellationToken.None);
            return (null, run.Record());
        }
        finally
        {
            turn.Release();
        }
    }

    /// <summary>
    /// <c>ConfirmPairing</c> (LOCAL-SPEC.md §6.2): <c>Invalid</c> while Waiting, <c>Ok</c> and unchanged while Confirming,
    /// <c>NotFound</c> for a pairing that is not the current one or has ended. When both sides have confirmed, the results
    /// are stored before the last frame is sent.
    /// </summary>
    /// <param name="pairing">The pairing's id.</param>
    /// <param name="cancellationToken">Cancels waiting for the lock.</param>
    /// <returns>The status and the pairing after the command.</returns>
    public async Task<(Status Status, Pairing? Pairing)> ConfirmAsync(uint pairing, CancellationToken cancellationToken)
    {
        await turn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (current is not { } run || run.Id != pairing)
                return (Status.NotFound, null);
            PairingStep step = run.Machine.OnConfirm(time.GetUtcNow());
            await TakeAsync(run, step).ConfigureAwait(false);
            return (StatusOf(step.Result), run.Record());
        }
        finally
        {
            turn.Release();
        }
    }

    /// <summary><c>CancelPairing</c> (LOCAL-SPEC.md §6.2): the pairing is Cancelled and the other side told so.</summary>
    /// <param name="pairing">The pairing's id.</param>
    /// <param name="cancellationToken">Cancels waiting for the lock.</param>
    /// <returns>The status and the pairing after the command.</returns>
    public async Task<(Status Status, Pairing? Pairing)> CancelAsync(uint pairing, CancellationToken cancellationToken)
    {
        await turn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (current is not { } run || run.Id != pairing)
                return (Status.NotFound, null);
            PairingStep step = run.Machine.OnCancel(time.GetUtcNow());
            await TakeAsync(run, step).ConfigureAwait(false);
            return (StatusOf(step.Result), run.Record());
        }
        finally
        {
            turn.Release();
        }
    }

    /// <summary>
    /// Serves a connection whose handshake negotiated the pairing ALPN: switches it to <c>axiom-mesh-pair/1</c> before any
    /// AMP/1 frame (PAIR-SPEC.md §5.1), takes it for the current pairing or answers <c>PairCancel(Busy)</c>, and runs it
    /// until it ends. A connection that reaches a node with no pairing at all closes without a byte.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="mesh">The AMP/1 session LWSE opened on it, which has sent and read nothing.</param>
    /// <param name="peer">What the handshake proved.</param>
    /// <param name="stopping">Cancelled when the node drains.</param>
    /// <returns>A task that completes when the connection may close.</returns>
    public async ValueTask ServeAsync(ConnectionContext connection, Session<MeshMessage, MeshMessage> mesh, PeerIdentity peer, CancellationToken stopping)
    {
        PairingRun? run;
        PairingLink? link = null;
        await turn.WaitAsync(stopping).ConfigureAwait(false);
        try
        {
            run = current;
            if (run is null)
            {
                connection.Abort();
                return;
            }

            Session<PairMessage, PairMessage> pairing = await mesh.SwitchAsync(PairProtocol.Definition, stopping).ConfigureAwait(false);
            if (!Takes(run, peer, connection.Remote))
            {
                await SendQuietlyAsync(pairing, new PairCancel(PairCancelReason.Busy, "this side runs another pairing")).ConfigureAwait(false);
                facts.Record(NodeFactKind.PairingChanged, connection.Id, connection.Remote, peer.Device, "a second pairing connection was answered Busy");
                return;
            }

            link = new PairingLink(pairing, peer, connection.Id, connection.Remote);
            run.Link = link;
            run.EverLinked = true;
            await SendQuietlyAsync(pairing, OwnHello(run)).ConfigureAwait(false);
        }
        finally
        {
            turn.Release();
        }

        using (link)
            await RunLinkAsync(run, link, stopping).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        current?.Dispose();
        gate.Set(PairingWindow.Closed);
    }

    // The status a command is answered with (LOCAL-SPEC.md §6.2).
    private static Status StatusOf(PairingResult result) => result switch
    {
        PairingResult.Ok => Status.Ok,
        PairingResult.Invalid => Status.Invalid,
        _ => Status.NotFound,
    };

    // PAIR-SPEC.md §2.2: up to 8 addresses, IPv4 first, as many as fit the 512 characters of a code.
    private static IReadOnlyList<PairingAddress> CodeAddresses(IReadOnlyList<IPEndPoint> endpoints, MeshMode mode, PublicKey key)
    {
        var addresses = endpoints
            .OrderBy(e => e.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 0 : 1)
            .Select(e => new PairingAddress(e.Address.ToString(), (ushort)e.Port))
            .Where(a => a.IsWellFormed)
            .Distinct()
            .Take(PairingCode.MaxAddresses)
            .ToList();
        while (addresses.Count > 0 && new PairingCode(mode, addresses, key).Text.Length > PairingCode.MaxLength)
            addresses.RemoveAt(addresses.Count - 1);
        return addresses;
    }

    // A connection is taken when the pairing waits for one and holds none: on the showing side any that the window let
    // through, on the entering side the one it dialled.
    private static bool Takes(PairingRun run, PeerIdentity peer, IPEndPoint remote) =>
        run.Machine.AwaitsPeer && run.Link is null
        && (peer.Role == TlsRole.Server ? !run.IsEntering : run.IsEntering && remote.Equals(run.Dialled));

    private static async ValueTask SendQuietlyAsync(Session<PairMessage, PairMessage> session, PairMessage message)
    {
        try
        {
            await session.WriteAsync(message, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ProtoStreamException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The other side is gone; the read loop sees the end.
        }
    }

    // PAIR-SPEC.md §5.2: the device being enrolled sends its proposed name and no user; every other side its user's name,
    // key and current Roster.
    private PairHello OwnHello(PairingRun run)
    {
        MeshNode running = Node;
        DeviceId device = running.Credentials.Credential!.Device;
        if (run.Machine.Mode == MeshMode.ShowDevice)
            return new PairHello(MeshMode.ShowDevice, device, deviceName, null, []);

        TrustSnapshot trust = running.Trust;
        SelfIdentity self = trust.Self!;
        VerifiedRoster roster = trust.TryGetUser(self.User, out UserTrust? user) && user.Roster is { } held
            ? held
            : throw new InvalidOperationException("The own user's Roster is not held, though the identity is.");
        return new PairHello(run.Machine.Mode, device, Shorten(self.UserName), self.UserKey, roster.Bytes.Span);
    }

    private static string Shorten(string name)
    {
        while (System.Text.Encoding.UTF8.GetByteCount(name) > Limits.MaxDeviceNameLength)
            name = name[..^1];
        return name;
    }

    // The entering side (PAIR-SPEC.md §4.1): the code's addresses in order, each pinned to the code's key, until one
    // carried a pairing connection; none did, the pairing fails.
    private async Task EnterAsync(PairingRun run)
    {
        PairingCode code = run.Entered!;
        foreach (PairingAddress address in code.Addresses)
        {
            if (run.Stopping.IsCancellationRequested || run.EverLinked)
                break;
            IReadOnlyList<IPAddress> resolved = IPAddress.TryParse(address.Host, out IPAddress? ip) ? [ip]
                : await ResolveAsync(address.Host, run.Stopping).ConfigureAwait(false);
            foreach (IPAddress each in resolved)
            {
                if (run.Stopping.IsCancellationRequested || run.EverLinked)
                    break;
                var endpoint = new IPEndPoint(each, address.Port);
                run.Dialled = endpoint;
                gate.Set(PairingWindow.Entering(code.DeviceKey, endpoint));
                await dialler.DialPairingAsync(endpoint, run.Stopping).ConfigureAwait(false);
            }
        }

        await turn.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(current, run) && !run.EverLinked && !run.Machine.IsEnded)
            {
                var refusal = new PairCancel(PairCancelReason.Unspecified, "no address of the code answered with the code's device key");
                await TakeAsync(run, run.Machine.OnPeerRefused(refusal, time.GetUtcNow())).ConfigureAwait(false);
            }

            if (ReferenceEquals(current, run) && gate.Current.EnteringKey is not null)
                gate.Set(PairingWindow.Closed);
        }
        finally
        {
            turn.Release();
        }
    }

    // A host name of the code (PAIR-SPEC.md §2.2) stands for the addresses the dialler resolves it to; a cancelled pairing
    // resolves nothing more.
    private async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken stopping)
    {
        try
        {
            return await dialler.ResolveAsync(host, stopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            return [];
        }
    }

    // One pairing connection, from the first PairHello to its close.
    private async Task RunLinkAsync(PairingRun run, PairingLink link, CancellationToken stopping)
    {
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(stopping, link.Ending);
        string ended = "the other side closed";
        try
        {
            while (true)
            {
                ProtocolReadResult<PairMessage> read = await link.Session.ReadAsync(reading.Token).ConfigureAwait(false);
                if (read.IsCompleted || read.Message is null)
                    break;
                if (await OnMessageAsync(run, link, read.Message).ConfigureAwait(false))
                {
                    ended = "this side closed";
                    break;
                }
            }
        }
        catch (Axiom.ProtoStream.Errors.ProtocolViolationException violation)
        {
            // The definition answered PairCancel(Violation) or PairCancel(Expired) already.
            ended = "violation: " + violation.Violation.Detail;
            await OnBrokenAsync(run, link, violation.Violation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (reading.IsCancellationRequested)
        {
            ended = stopping.IsCancellationRequested ? "the node stops" : "this side closed";
        }
        catch (Exception ex) when (ex is ProtoStreamException or IOException or ObjectDisposedException)
        {
            ended = ex.Message;
        }

        await DetachAsync(run, link, ended).ConfigureAwait(false);
    }

    private async Task<bool> OnMessageAsync(PairingRun run, PairingLink link, PairMessage message)
    {
        await turn.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(current, run) || !ReferenceEquals(run.Link, link))
                return true;
            DateTimeOffset now = time.GetUtcNow();
            PairingStep step = message switch
            {
                PairHello hello => OnHello(run, link, hello, now),
                PairConfirm confirm => await OnConfirmAsync(run, confirm, now).ConfigureAwait(false),
                PairCancel cancel => run.Machine.OnPeerCancel(cancel, now),
                _ => run.Machine.OnTick(now),
            };
            return await TakeAsync(run, step).ConfigureAwait(false);
        }
        finally
        {
            turn.Release();
        }
    }

    // PAIR-SPEC.md §7.1, §8: the other side's PairHello checked, the SAS derived from both user keys and both TLS keys.
    private PairingStep OnHello(PairingRun run, PairingLink link, PairHello hello, DateTimeOffset now)
    {
        MeshNode running = Node;
        MeshMode mode = run.Machine.Mode;
        if (!PairingRules.TryAcceptHello(hello, link.Peer.Key, mode, out PairPeer? peer, out PairCancel? refusal))
            return run.Machine.OnPeerRefused(refusal, now);

        TrustSnapshot trust = running.Trust;
        SelfIdentity? self = trust.Self;
        if (peer.UserKey is { } other && self is not null && other.Equals(self.UserKey))
            return run.Machine.OnPeerRefused(new PairCancel(PairCancelReason.Refused, "the other side is a device of the same user; enrol it instead"), now);
        if (mode == MeshMode.AddDevice && !CanEnrol(trust, self!, peer, now))
            return run.Machine.OnPeerRefused(new PairCancel(PairCancelReason.Refused, "the device was removed from this user's Roster and cannot be listed again"), now);

        run.Peer = peer;
        DeviceCredential own = running.Credentials.Credential!;
        string sas = Sas.Derive(self?.UserKey, own.Key, peer.UserKey, link.Peer.Key);
        return run.Machine.OnPeerHello(sas, now);
    }

    private static bool CanEnrol(TrustSnapshot trust, SelfIdentity self, PairPeer device, DateTimeOffset now) =>
        trust.TryGetUser(self.User, out UserTrust? user) && user.Roster is { } roster
        && PairingRules.TryEnrol(roster, device, user.RemovedEver, (ulong)now.ToUnixTimeMilliseconds(), out _);

    // PAIR-SPEC.md §6.2, §7.2: on the device being enrolled, an enrolment that arrives once its user confirmed is checked
    // and adopted before the pairing is Paired; anywhere else the confirmation is remembered, or completes the pairing.
    private async Task<PairingStep> OnConfirmAsync(PairingRun run, PairConfirm confirm, DateTimeOffset now)
    {
        if (run.Machine.Mode != MeshMode.ShowDevice || run.Machine.State != MeshState.Confirming)
            return run.Machine.OnPeerConfirm(now);

        MeshNode running = Node;
        if (!PairingRules.TryAcceptEnrolment(confirm, run.Peer!, running.Credentials.Credential!.Key, out VerifiedRoster? roster, out PairCancel? refusal))
            return run.Machine.OnPeerRefused(refusal, now);
        await running.AdoptEnrolmentAsync(roster, run.Peer!.Name, CancellationToken.None).ConfigureAwait(false);
        return run.Machine.OnPeerConfirm(now);
    }

    // Takes a step: stores what a pairing that becomes Paired leaves (PAIR-SPEC.md §9) before anything is sent, sends what
    // the step says, reports the change, and says whether the connection is to close now.
    private async Task<bool> TakeAsync(PairingRun run, PairingStep step, bool sendFrames = true)
    {
        PairingMachine before = run.Machine;
        VerifiedRoster? enrolment = null;
        if (step.Action == PairingAction.SendEnrolment)
        {
            (enrolment, string? problem) = await EnrolAsync(run).ConfigureAwait(false);
            if (enrolment is null)
                step = run.Machine.OnPeerRefused(new PairCancel(PairCancelReason.Refused, problem!), time.GetUtcNow());
        }
        else if (step.Next.State == MeshState.Paired && before.State != MeshState.Paired && run.Peer?.Roster is { } roster
            && run.Machine.Mode is MeshMode.ShowUser or MeshMode.JoinUser)
        {
            await Node.AcceptUserAsync(roster, run.Peer!.Name, CancellationToken.None).ConfigureAwait(false);
        }

        run.Machine = step.Next;
        if (sendFrames && run.Link is { } link)
        {
            PairMessage? frame = step.Action switch
            {
                PairingAction.SendConfirm => PairConfirm.Plain,
                PairingAction.SendEnrolment when enrolment is not null => PairConfirm.Enrolling(enrolment),
                PairingAction.SendCancel => step.Cancel,
                _ => null,
            };
            if (frame is not null)
                await SendQuietlyAsync(link.Session, frame).ConfigureAwait(false);
        }

        if (!ReferenceEquals(before, run.Machine))
            Changed(run);
        if (run.Machine.State != MeshState.Waiting && gate.Current.IsShowing)
            gate.Set(PairingWindow.Closed);

        bool close = run.Machine.IsEnded && (run.Machine.State != MeshState.Paired || run.Link is not { IsEntering: true });
        if (run.Machine.IsEnded)
        {
            run.Expiry?.Dispose();
            if (close)
                run.Link?.End();
            if (run.Link is null)
                run.Halt();
        }

        return close;
    }

    // PAIR-SPEC.md §9.2: the AddDevice side signs the current Roster with seq one higher and the device added, stores it as
    // its user's current one (which pushes it to every established peer, SPEC.md §7.6) and sends it.
    private async Task<(VerifiedRoster? Roster, string? Problem)> EnrolAsync(PairingRun run)
    {
        MeshNode running = Node;
        PairPeer device = run.Peer!;
        (Status status, VerifiedRoster? roster) = await running.WithUserKeyAsync<VerifiedRoster>(
            async (signer, ct) =>
            {
                TrustSnapshot trust = running.Trust;
                SelfIdentity self = trust.Self!;
                if (!trust.TryGetUser(self.User, out UserTrust? user) || user.Roster is not { } current
                    || !PairingRules.TryEnrol(current, device, user.RemovedEver, (ulong)time.GetUtcNow().ToUnixTimeMilliseconds(), out RosterDraft? draft))
                {
                    return (Status.Invalid, null);
                }

                VerifiedRoster next = draft.Sign(signer);
                await running.Store.PutAsync(next, ct).ConfigureAwait(false);
                return (Status.Ok, next);
            },
            CancellationToken.None).ConfigureAwait(false);
        return status switch
        {
            Status.Ok => (roster, null),
            Status.UserKeyAbsent => (null, "the user key is no longer on the adding device"),
            _ => (null, "the device cannot be listed in this user's Roster"),
        };
    }

    private async Task OnBrokenAsync(PairingRun run, PairingLink link, Violation violation)
    {
        await turn.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(current, run) || !ReferenceEquals(run.Link, link) || run.Machine.IsEnded)
                return;
            PairCancel ended = violation.Code == ViolationCode.Timeout
                ? new PairCancel(PairCancelReason.Expired, "the pairing expired: nothing arrived from the other side in time")
                : new PairCancel(PairCancelReason.Violation, Axiom.Mesh.Protocol.Bye.Shorten("the other side broke the protocol: " + violation.Detail));
            PairingStep step = run.Machine.OnPeerRefused(ended, time.GetUtcNow());

            // The definition sent the PairCancel already.
            await TakeAsync(run, step, sendFrames: false).ConfigureAwait(false);
        }
        finally
        {
            turn.Release();
        }
    }

    // The connection ended: a showing side that still waits keeps waiting (the window reopens), anything else unended
    // fails; an entering side whose pairing completed now dials the other side under AMP/1.
    private async Task DetachAsync(PairingRun run, PairingLink link, string ended)
    {
        await turn.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(run.Link, link))
                return;
            run.Link = null;
            facts.Record(NodeFactKind.PairingChanged, link.Connection, link.Remote, link.Peer.Device, "the pairing connection ended: " + ended);
            if (!ReferenceEquals(current, run))
                return;

            if (!run.Machine.IsEnded)
            {
                await TakeAsync(run, run.Machine.OnDisconnected(time.GetUtcNow())).ConfigureAwait(false);
                if (run.Machine.State == MeshState.Waiting && !run.IsEntering)
                {
                    run.EverLinked = false;
                    gate.Set(PairingWindow.Showing);
                }
            }

            if (run.Machine.State == MeshState.Paired && link.IsEntering && run.Peer is { } peer)
            {
                UserId user = peer.UserKey?.AsUserId() ?? Node.Trust.Self!.User;
                table.DialPaired(peer.Device, user, link.Remote);
            }

            if (run.Machine.IsEnded)
                run.Halt();
        }
        finally
        {
            turn.Release();
        }
    }

    private void Expire(PairingRun run) => _ = ExpireAsync(run);

    // PAIR-SPEC.md §6.3: a pairing not final at its expiry fails, and the other side is told so.
    private async Task ExpireAsync(PairingRun run)
    {
        await turn.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(current, run) && !run.Machine.IsEnded)
                await TakeAsync(run, run.Machine.OnTick(time.GetUtcNow())).ConfigureAwait(false);
        }
        finally
        {
            turn.Release();
        }
    }

    private void Changed(PairingRun run)
    {
        Pairing record = run.Record();
        string detail = record.Failure.Length == 0 ? $"{record.Mode} {record.State}" : $"{record.Mode} {record.State}: {record.Failure}";
        facts.Record(NodeFactKind.PairingChanged, run.Link?.Connection ?? 0, run.Link?.Remote, record.RemoteDeviceId, detail);
        changed(record);
    }
}
