using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh pair cancel</c>: ends the current pairing on both sides; nothing is stored (PAIR-SPEC.md §6.2).</summary>
internal sealed record PairCancelCommand : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        Pairing current = await context.SendAsync(new GetPairing()).ConfigureAwait(false);
        Pairing cancelled = await context.SendAsync(new CancelPairing { PairingId = current.PairingId }).ConfigureAwait(false);
        return Print(context, PairingView.Text(cancelled));
    }
}
