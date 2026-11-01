using System.Collections.Generic;

namespace Axiom.Mesh.Host;

/// <summary>
/// How a spawned service's process is started (LOCAL-SPEC.md §4.2): the executable, its arguments, its working directory
/// (the service's state directory) and the variables added to the daemon's own environment, the token among them. It never
/// prints a variable's value, so that the token cannot reach a log through it.
/// </summary>
internal sealed class ChildStart
{
    /// <summary>The executable, absolute.</summary>
    public required string Executable { get; init; }

    /// <summary>The arguments after the executable.</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>The working directory: the service's state directory.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>The variables added to the daemon's environment for the child.</summary>
    public required IReadOnlyDictionary<string, string> Environment { get; init; }

    /// <summary>The executable, the arguments and the names of the added variables; never their values.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => $"{Executable} {string.Join(' ', Arguments)} (in {WorkingDirectory}, with {string.Join(", ", Environment.Keys)})";
}
