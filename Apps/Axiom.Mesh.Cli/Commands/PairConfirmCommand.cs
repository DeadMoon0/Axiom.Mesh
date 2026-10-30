using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh pair confirm</c>: this side's user saw the same string on both screens (PAIR-SPEC.md §6.2). Waits until
/// the other side confirmed too and the pairing is Paired, or ended otherwise.
/// </summary>
internal sealed record PairConfirmCommand : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        await using PairingWatch watch = await PairingWatch.StartAsync(context).ConfigureAwait(false);
        Pairing current = await context.SendAsync(new GetPairing()).ConfigureAwait(false);
        Pairing confirmed = await context.SendAsync(new ConfirmPairing { PairingId = current.PairingId }).ConfigureAwait(false);
        if (confirmed.State == PairingState.Confirming)
            context.Error.WriteLine(PairingView.Next(confirmed));
        Pairing ended = await watch.UntilAsync(confirmed.PairingId, PairingView.IsEnded).ConfigureAwait(false);
        return PairingResult.Report(context, ended);
    }
}
