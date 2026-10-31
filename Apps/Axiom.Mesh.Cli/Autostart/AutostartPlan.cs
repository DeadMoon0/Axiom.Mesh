using System.Collections.Generic;

namespace Axiom.Mesh.Cli;

/// <summary>
/// What <c>axiom-mesh host install</c> or <c>uninstall</c> does, decided before anything is touched: the registration,
/// the daemon's command line, and the steps in order. <see cref="AutostartShell"/> runs the steps; nothing else decides.
/// </summary>
internal sealed record AutostartPlan
{
    /// <summary>Which part of the operating system starts the Host.</summary>
    public required AutostartSystem System { get; init; }

    /// <summary>What is registered: the task's name, or the unit file's path.</summary>
    public required string Registration { get; init; }

    /// <summary>The daemon's executable, absolute.</summary>
    public required string Daemon { get; init; }

    /// <summary>The daemon's arguments: <c>run</c>, and <c>--home</c> with a home of its own.</summary>
    public required IReadOnlyList<string> DaemonArguments { get; init; }

    /// <summary>Whether the plan starts the Host now (install without <c>--no-start</c>).</summary>
    public required bool Starts { get; init; }

    /// <summary>The steps, in order.</summary>
    public required IReadOnlyList<AutostartStep> Steps { get; init; }

    /// <summary>Lines the user should read: an environment variable the registered daemon will not see, for one.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}
