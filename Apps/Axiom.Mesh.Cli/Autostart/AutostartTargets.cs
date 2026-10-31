using System;
using System.IO;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>Builds the <see cref="AutostartTarget"/> from what the machine and the environment say. Pure.</summary>
internal static class AutostartTargets
{
    /// <summary>The target: the daemon, the home <c>AXIOM_MESH_HOME</c> names, the account, the directories.</summary>
    /// <param name="machine">The platform, account, default daemon and temporary directory.</param>
    /// <param name="environment">Reads an environment variable.</param>
    /// <param name="daemon">The daemon's executable, absolute.</param>
    /// <param name="currentDirectory">The directory a relative home is resolved against.</param>
    /// <returns>The target.</returns>
    /// <exception cref="CliFailure">On Linux neither <c>XDG_CONFIG_HOME</c> nor <c>HOME</c> names the unit directory.</exception>
    public static AutostartTarget Of(CliMachine machine, Func<string, string?> environment, string daemon, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(environment);
        string? units = AutostartPlans.UnitDirectory(environment);
        if (machine.Platform == LocalPlatform.Linux && units is null)
            throw CliFailure.Problem("neither XDG_CONFIG_HOME nor HOME is set, so the systemd user unit directory is unknown");
        return new AutostartTarget
        {
            Daemon = daemon,
            Home = environment(CliPaths.HomeVariable) is { Length: > 0 } home ? Path.TrimEndingDirectorySeparator(Absolute(home, currentDirectory)) : null,
            Account = machine.Account,
            UnitDirectory = units ?? string.Empty,
            PipeVariable = environment(PipeLocation.EnvironmentVariable),
            TemporaryDirectory = machine.TemporaryDirectory,
        };
    }

    /// <summary>A path as given when it is rooted, else resolved against the current directory.</summary>
    /// <param name="path">The path.</param>
    /// <param name="currentDirectory">The current directory.</param>
    /// <returns>The path.</returns>
    public static string Absolute(string path, string currentDirectory) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(path, currentDirectory);
}
