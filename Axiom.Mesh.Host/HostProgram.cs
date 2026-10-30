using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;

namespace Axiom.Mesh.Host;

/// <summary>
/// The command line of <c>axiom-meshd</c>, the daemon. Everything the process reads from outside (its arguments, its
/// version, its environment, its output streams, the signal to stop) is handed in, so a command is a function of its
/// inputs and a test runs the daemon in process.
/// </summary>
public static class HostProgram
{
    /// <summary>The name the daemon is started by.</summary>
    public const string CommandName = "axiom-meshd";

    /// <summary>Runs a command that needs no environment: <c>version</c>, or the usage.</summary>
    /// <param name="args">The arguments after the program name.</param>
    /// <param name="output">Where results go (standard output).</param>
    /// <param name="error">Where usage and failures go (standard error).</param>
    /// <param name="version">The daemon's informational version.</param>
    /// <returns>What the process exits with; <c>run</c> is not run here and prints the usage.</returns>
    public static HostExitCode Run(IReadOnlyList<string> args, TextWriter output, TextWriter error, string version)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (args is ["version"])
        {
            output.WriteLine(version);
            return HostExitCode.Ok;
        }

        return Usage(error);
    }

    /// <summary>Runs one command line: <c>version</c>, or <c>run [--home &lt;directory&gt;]</c> until <paramref name="stop"/>.</summary>
    /// <param name="args">The arguments after the program name.</param>
    /// <param name="output">Where results go (standard output).</param>
    /// <param name="error">Where usage and failures go (standard error).</param>
    /// <param name="version">The daemon's informational version.</param>
    /// <param name="environment">Reads an environment variable.</param>
    /// <param name="stop">Cancelled by Ctrl-C or SIGTERM: the daemon drains its clients and stops.</param>
    /// <returns>What the process exits with.</returns>
    public static async Task<HostExitCode> RunAsync(IReadOnlyList<string> args, TextWriter output, TextWriter error, string version, Func<string, string?> environment, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);
        if (args is not ["run", ..])
            return Run(args, output, error, version);
        if (args is not (["run"] or ["run", "--home", _]))
            return Usage(error);

        try
        {
            HostPaths paths = HostPaths.Resolve(HostPlatform.Current, environment, args.Count == 3 ? args[2] : null);
            HostRuntime runtime = await HostRuntime.StartAsync(new HostStart { Paths = paths, Environment = environment, Version = version }, stop).ConfigureAwait(false);
            await using (runtime.ConfigureAwait(false))
            {
                Describe(output, version, runtime);
                try
                {
                    await Task.Delay(Timeout.Infinite, stop).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    output.WriteLine($"{CommandName} stops: local clients get {LocalPortOptions.Default.DrainGrace.TotalSeconds:0} s to finish.");
                }
            }

            return HostExitCode.Ok;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return HostExitCode.Ok;
        }
        catch (HostException ex)
        {
            error.WriteLine(ex.Message);
            return ex.Exit;
        }
        catch (MeshConfigurationException ex)
        {
            error.WriteLine(ex.Message);
            return HostExitCode.Configuration;
        }
        catch (MeshException ex)
        {
            error.WriteLine(ex.Message);
            return ex.Fault == MeshFault.Configuration ? HostExitCode.Configuration : HostExitCode.Environment;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SocketException)
        {
            error.WriteLine($"{CommandName} cannot start: {ex.Message}");
            error.WriteLine("  Check that the home directory is the user's and the mesh port is free (mesh.port in config.json), then start again.");
            return HostExitCode.Environment;
        }
    }

    private static void Describe(TextWriter output, string version, HostRuntime runtime)
    {
        output.WriteLine($"{CommandName} {version} runs.");
        output.WriteLine($"  home      {runtime.Paths.Home}");
        output.WriteLine($"  config    {runtime.Paths.ConfigFile}");
        output.WriteLine($"  pipe      {runtime.Pipe}");
        output.WriteLine($"  mesh      {runtime.Node.MeshEndpoint}");
        output.WriteLine($"  logs      {runtime.Paths.Logs}");
        output.WriteLine(runtime.Node.Identity is { } identity
            ? $"  identity  device {identity.Device} of user {identity.User}"
            : "  identity  none yet: create one with axiom-mesh identity create --user <name> --device <name>");
        output.WriteLine("Press Ctrl-C to stop.");
    }

    private static HostExitCode Usage(TextWriter error)
    {
        error.WriteLine("usage: " + CommandName + " run [--home <directory>]");
        error.WriteLine("       " + CommandName + " version");
        error.WriteLine();
        error.WriteLine("  run       run the daemon in the foreground until Ctrl-C or SIGTERM; --home keeps everything in one directory");
        error.WriteLine("  version   print the version of this daemon");
        return HostExitCode.Usage;
    }
}
