using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Testing;

/// <summary>
/// One pairing driven over two nodes' local ports as two people at their screens would (PAIR-SPEC.md, LOCAL-SPEC.md §6.2):
/// one shows a code, the other enters it, or names the showing device by the id discovery announced (PAIR-SPEC.md §2.4);
/// both wait until the short authentication strings show, compare them and confirm. Waits re-read the pairing after every
/// <see cref="NodeFactKind.PairingChanged"/> fact, never by time. Shared by every mesh fixture, in memory or on sockets.
/// </summary>
public static class MeshPairing
{
    /// <summary>Runs a pairing to its end, the entering side given the code; the dial that follows under AMP/1 is not awaited.</summary>
    /// <param name="showing">The showing node: its name, the node and a user client of it.</param>
    /// <param name="showMode">The showing side's mode: <c>ShowUser</c> or <c>ShowDevice</c>.</param>
    /// <param name="entering">The entering node: its name, the node and a user client of it.</param>
    /// <param name="enterMode">The entering side's mode: <c>JoinUser</c> or <c>AddDevice</c>.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The short authentication string both sides showed.</returns>
    /// <exception cref="TestMeshException">A side refused a step, the strings differ, or a step did not happen within <see cref="TestMesh.Deadline"/>.</exception>
    public static Task<string> RunAsync((string Name, MeshNode Node, LocalClient Client) showing, PairingMode showMode, (string Name, MeshNode Node, LocalClient Client) entering, PairingMode enterMode, CancellationToken cancellationToken) =>
        RunAsync(showing, showMode, entering, enterMode, PairingEntry.ByCode, cancellationToken);

    /// <summary>Runs a pairing to its end; the dial that follows under AMP/1 is not awaited.</summary>
    /// <param name="showing">The showing node: its name, the node and a user client of it.</param>
    /// <param name="showMode">The showing side's mode: <c>ShowUser</c> or <c>ShowDevice</c>.</param>
    /// <param name="entering">The entering node: its name, the node and a user client of it.</param>
    /// <param name="enterMode">The entering side's mode: <c>JoinUser</c> or <c>AddDevice</c>.</param>
    /// <param name="entry">How the entering side names the showing one: by its code, or by the device id and addresses <c>ListDiscovered</c> shows.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The short authentication string both sides showed.</returns>
    /// <exception cref="TestMeshException">A side refused a step, the strings differ, or a step did not happen within <see cref="TestMesh.Deadline"/>.</exception>
    public static async Task<string> RunAsync((string Name, MeshNode Node, LocalClient Client) showing, PairingMode showMode, (string Name, MeshNode Node, LocalClient Client) entering, PairingMode enterMode, PairingEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(showing.Client);
        ArgumentNullException.ThrowIfNull(entering.Client);
        Pairing shown = Expect(showing.Name, await showing.Client.SendAsync(new StartPairing { Mode = showMode }, cancellationToken).ConfigureAwait(false));
        LocalResult<Pairing> started = entry == PairingEntry.ByCode
            ? await entering.Client.SendAsync(new StartPairing { Mode = enterMode, Code = shown.Code }, cancellationToken).ConfigureAwait(false)
            : await entering.Client.SendAsync(await ByDiscoveryAsync(showing, entering, enterMode, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        Expect(entering.Name, started);
        shown = await WaitForAsync(showing, p => p.State != PairingState.Waiting, "the other side to connect", cancellationToken).ConfigureAwait(false);
        Pairing entered = await WaitForAsync(entering, p => p.State != PairingState.Waiting, "the showing device to answer", cancellationToken).ConfigureAwait(false);
        if (shown.State != PairingState.Confirm || entered.State != PairingState.Confirm || shown.Sas != entered.Sas)
        {
            throw new TestMeshException(
                $"The pairing did not reach Confirm with equal strings: {showing.Name} {shown.State} '{shown.Sas}' {shown.Failure}, {entering.Name} {entered.State} '{entered.Sas}' {entered.Failure}.",
                "Read both nodes' PairingChanged facts: a Failed state names the check that refused.");
        }

        Expect(showing.Name, await showing.Client.SendAsync(new ConfirmPairing { PairingId = shown.PairingId }, cancellationToken).ConfigureAwait(false));
        Expect(entering.Name, await entering.Client.SendAsync(new ConfirmPairing { PairingId = entered.PairingId }, cancellationToken).ConfigureAwait(false));
        await WaitForAsync(showing, p => p.State is not (PairingState.Confirm or PairingState.Confirming), "the pairing to end", cancellationToken).ConfigureAwait(false);
        Pairing done = await WaitForAsync(entering, p => p.State is not (PairingState.Confirm or PairingState.Confirming), "the pairing to end", cancellationToken).ConfigureAwait(false);
        if (done.State != PairingState.Paired)
            throw new TestMeshException($"{entering.Name} ended the pairing {done.State}: {done.Failure}", "Read both nodes' PairingChanged facts.");
        return shown.Sas;
    }

    /// <summary>
    /// Waits until a node's current pairing (<c>GetPairing</c>, LOCAL-SPEC.md §6.2) satisfies a condition, reading it again
    /// after every <see cref="NodeFactKind.PairingChanged"/> fact.
    /// </summary>
    /// <param name="node">The node: its name, the node and a user client of it.</param>
    /// <param name="condition">What must hold.</param>
    /// <param name="what">What is awaited, for the failure message.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The pairing once it holds.</returns>
    /// <exception cref="TestMeshException">It did not hold within <see cref="TestMesh.Deadline"/>.</exception>
    public static async Task<Pairing> WaitForAsync((string Name, MeshNode Node, LocalClient Client) node, Func<Pairing, bool> condition, string what, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node.Node);
        ArgumentNullException.ThrowIfNull(node.Client);
        ArgumentNullException.ThrowIfNull(condition);
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        try
        {
            while (true)
            {
                long seen = node.Node.Facts.LastSequence;
                LocalResult<Pairing> read = await node.Client.SendAsync(new GetPairing(), linked.Token).ConfigureAwait(false);
                if (read.IsOk && condition(read.Value))
                    return read.Value;
                await node.Node.Facts.WaitForAsync(f => f.Kind == NodeFactKind.PairingChanged, seen, linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TestMeshException(
                $"Waited {TestMesh.Deadline.TotalSeconds:0} s for {what} on {node.Name}, in vain. Its last facts: {string.Join(" | ", node.Node.Facts.Recent().TakeLast(16))}.",
                "Read the node's PairingChanged facts: a Failed state names the check that refused.");
        }
    }

    /// <summary>
    /// The <c>StartPairingByDevice</c> that names a showing device the way a user who typed no code would (PAIR-SPEC.md §2.4): the
    /// device id and the addresses <c>ListDiscovered</c> (LOCAL-SPEC.md §6.6) shows for it on the entering node, read
    /// again after every fact until discovery has heard the device.
    /// </summary>
    /// <param name="showing">The showing node.</param>
    /// <param name="entering">The entering node, whose discovery must have heard the showing one.</param>
    /// <param name="enterMode">The entering side's mode.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The request.</returns>
    /// <exception cref="TestMeshException">Discovery on the entering node did not list the showing device within <see cref="TestMesh.Deadline"/>.</exception>
    public static async Task<StartPairingByDevice> ByDiscoveryAsync((string Name, MeshNode Node, LocalClient Client) showing, (string Name, MeshNode Node, LocalClient Client) entering, PairingMode enterMode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(showing.Node);
        ArgumentNullException.ThrowIfNull(entering.Node);
        ArgumentNullException.ThrowIfNull(entering.Client);
        DeviceId device = showing.Node.Device ?? throw new TestMeshException($"{showing.Name} has no device key yet, so discovery cannot have announced it.", "Show a code there first; a device being enrolled is named by its code.");
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        try
        {
            while (true)
            {
                long seen = entering.Node.Facts.LastSequence;
                LocalResult<IReadOnlyList<Peer>> read = await entering.Client.SendAsync(new ListDiscovered(), linked.Token).ConfigureAwait(false);
                if (read.IsOk && read.Value.FirstOrDefault(p => p.DeviceId == device && p.Addresses.Count != 0) is { } heard)
                    return new StartPairingByDevice { Mode = enterMode, Device = heard.DeviceId, Addresses = heard.Addresses };

                // Discovery reports a stranger to the peer table without a fact of its own, so a short wait is taken too.
                Task fact = entering.Node.Facts.WaitForAsync(_ => true, seen, linked.Token);
                await Task.WhenAny(fact, Task.Delay(TimeSpan.FromMilliseconds(50), linked.Token)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TestMeshException(
                $"Waited {TestMesh.Deadline.TotalSeconds:0} s for discovery on {entering.Name} to list {showing.Name} ({device}), in vain.",
                "Both nodes must share a discovery board and not be severed; a silent node is never heard.");
        }
    }

    private static Pairing Expect(string name, LocalResult<Pairing> result) =>
        result.IsOk ? result.Value
        : throw new TestMeshException($"{name} refused a pairing request with {result.Status}: {result.Error?.Message}", "Read the node's PairingChanged facts and the request's status.");
}
