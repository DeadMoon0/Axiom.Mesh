namespace Axiom.Mesh.Cli;

/// <summary>
/// Stops every process of this user that runs the daemon's executable. On Windows ending the scheduled task ends the
/// console host that hides the daemon's window, not the daemon itself; this step ends the daemon.
/// </summary>
/// <param name="Daemon">The daemon's executable, absolute.</param>
internal sealed record StopStep(string Daemon) : AutostartStep
{
    /// <inheritdoc />
    public override string Describe() => "stop every running " + Daemon;
}
