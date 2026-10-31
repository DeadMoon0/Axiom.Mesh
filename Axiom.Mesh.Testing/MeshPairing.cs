using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Testing;

/// <summary>
/// One pairing driven over two nodes' local ports as two people at their screens would (PAIR-SPEC.md, LOCAL-SPEC.md §6.2):
/// one shows a code, the other enters it, both wait until the short authentication strings show, compare them and confirm.
/// Waits re-read the pairing after every <see cref="NodeFactKind.PairingChanged"/> fact, never by time. Shared by every
/// mesh fixture, in memory or on sockets.
/// </summary>
public static class MeshPairing
{
    /// <summary>Runs a pairing to its end; the dial that follows under AMP/1 is not awaited.</summary>
    /// <param name="showing">The showing node: its name, the node and a user client of it.</param>
    /// <param name="showMode">The showing side's mode: <c>ShowUser</c> or <c>ShowDevice</c>.</param>
    /// <param name="entering">The entering node: its name, the node and a user client of it.</param>
    /// <param name="enterMode">The entering side's mode: <c>JoinUser</c> or <c>AddDevice</c>.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The short authentication string both sides showed.</returns>
    /// <exception cref="TestMeshException">A side refused a step, the strings differ, or a step did not happen within <see cref="TestMesh.Deadline"/>.</exception>
    public static async Task<string> RunAsync((string Name, MeshNode Node, LocalClient Client) showing, PairingMode showMode, (string Name, MeshNode Node, LocalClient Client) entering, PairingMode enterMode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(showing.Client);
        ArgumentNullException.ThrowIfNull(entering.Client);
        Pairing shown = Expect(showing.Name, await showing.Client.SendAsync(new StartPairing { Mode = showMode }, cancellationToken).ConfigureAwait(false));
        Expect(entering.Name, await entering.Client.SendAsync(new StartPairing { Mode = enterMode, Code = shown.Code }, cancellationToken).ConfigureAwait(false));
        shown = await WaitForAsync(showing, p => p.State != PairingState.Waiting, "the other side to connect", cancellationToken).ConfigureAwait(false);
        Pairing entered = await WaitForAsync(entering, p => p.State != PairingState.Waiting, "the code's device to answer", cancellationToken).ConfigureAwait(false);
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

    private static Pairing Expect(string name, LocalResult<Pairing> result) =>
        result.IsOk ? result.Value
        : throw new TestMeshException($"{name} refused a pairing request with {result.Status}: {result.Error?.Message}", "Read the node's PairingChanged facts and the request's status.");
}
