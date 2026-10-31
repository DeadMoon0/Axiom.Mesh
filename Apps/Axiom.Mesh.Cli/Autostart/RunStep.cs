using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Cli;

/// <summary>Runs a program of the operating system and waits for it.</summary>
/// <param name="Program">The program: <c>schtasks</c>, <c>systemctl</c>.</param>
/// <param name="Arguments">Its arguments, each passed as one.</param>
/// <param name="MayFail">Whether a non-zero exit is expected sometimes (ending a task that does not run) and ends nothing.</param>
/// <param name="Removes">Whether its success is what "something was registered" means for an uninstall.</param>
internal sealed record RunStep(string Program, IReadOnlyList<string> Arguments, bool MayFail, bool Removes) : AutostartStep
{
    /// <inheritdoc />
    public override string Describe() => string.Join(' ', Arguments.Select(Quote).Prepend(Program));

    // For reading only: an argument with a space is shown in quotes.
    private static string Quote(string argument) => argument.Contains(' ', System.StringComparison.Ordinal) ? $"\"{argument}\"" : argument;
}
