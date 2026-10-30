using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>How a pairing command ends: the pairing printed, what to do next on standard error, and a failed or cancelled pairing as exit 3.</summary>
internal static class PairingResult
{
    /// <summary>Reports a pairing where a command left it.</summary>
    /// <param name="context">The command's context.</param>
    /// <param name="pairing">The pairing.</param>
    /// <returns><see cref="CliExitCode.Ok"/> unless it failed or was cancelled, which leaves as a <see cref="CliFailure"/>.</returns>
    public static CliExitCode Report(CommandContext context, Pairing pairing)
    {
        foreach (string line in PairingView.Text(pairing))
            context.Output.WriteLine(line);
        if (pairing.State is PairingState.Failed or PairingState.Cancelled)
            throw CliFailure.Problem("the pairing " + PairingView.Next(pairing));
        context.Error.WriteLine(PairingView.Next(pairing));
        return CliExitCode.Ok;
    }
}
