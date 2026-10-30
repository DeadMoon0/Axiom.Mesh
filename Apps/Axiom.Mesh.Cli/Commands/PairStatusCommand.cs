using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh pair status</c>: the current or the last pairing, and what to do next.</summary>
internal sealed record PairStatusCommand : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        Pairing current = await context.SendAsync(new GetPairing()).ConfigureAwait(false);
        Print(context, PairingView.Text(current));
        context.Error.WriteLine(PairingView.Next(current));
        return CliExitCode.Ok;
    }
}
