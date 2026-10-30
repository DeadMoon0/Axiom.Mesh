using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh pair show [--device]</c>: starts a pairing that shows a code (PAIR-SPEC.md §3): <c>ShowUser</c> to pair
/// with another user, <c>ShowDevice</c> (with <c>--device</c>) to have this device enrolled by another device of its
/// user. Prints the code, waits until the other side connected, and prints the string to compare.
/// </summary>
/// <param name="Device">Whether this device is to be enrolled (mode <c>ShowDevice</c>).</param>
internal sealed record PairShowCommand(bool Device) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        await using PairingWatch watch = await PairingWatch.StartAsync(context).ConfigureAwait(false);
        Pairing started = await context.SendAsync(new StartPairing { Mode = Device ? PairingMode.ShowDevice : PairingMode.ShowUser }).ConfigureAwait(false);
        Print(context, started.Code);
        if (context.Environment.OutputIsTerminal)
            Print(context, PairingView.Box(started.Code));
        context.Error.WriteLine(PairingView.Next(started));

        Pairing connected = await watch.UntilAsync(started.PairingId, p => p.State != PairingState.Waiting).ConfigureAwait(false);
        return PairingResult.Report(context, connected);
    }
}
