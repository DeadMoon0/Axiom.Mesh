using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Service;

/// <summary>
/// The command line of a service executable (LOCAL-SPEC.md §9.1): <c>run</c> (the default) connects to the Host and serves
/// until stopped; <c>version</c> prints the version. A service's <c>Program.cs</c> is one line:
/// <c>return ServiceProgram.Run(new MyService(), args);</c>. Everything else a run reads is handed in through
/// <see cref="ServiceEnvironment"/>, so a test runs a service in process.
/// </summary>
public static class ServiceProgram
{
    /// <summary>Runs a service as its process: the real environment, Ctrl-C and SIGTERM, the console for version and usage.</summary>
    /// <param name="service">The service.</param>
    /// <param name="args">The process's arguments.</param>
    /// <returns>The process's exit code (<see cref="ServiceExitCode"/>).</returns>
    public static int Run(IMeshService service, string[] args)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(args);
        return (int)ServiceShell.RunAsync(service, args).GetAwaiter().GetResult();
    }

    /// <summary>Runs one command line of a service.</summary>
    /// <param name="service">The service.</param>
    /// <param name="args">The arguments after the program name: none or <c>run</c>, or <c>version</c>.</param>
    /// <param name="output">Where <c>version</c> prints (standard output).</param>
    /// <param name="error">Where the usage goes (standard error).</param>
    /// <param name="environment">The way to the Host, the state, the variables, the clock and the loggers.</param>
    /// <param name="stop">Cancelled by Ctrl-C or SIGTERM: the service says Bye and exits.</param>
    /// <returns>What the process exits with.</returns>
    public static async Task<ServiceExitCode> RunAsync(IMeshService service, IReadOnlyList<string> args, TextWriter output, TextWriter error, ServiceEnvironment environment, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(environment);
        switch (args)
        {
            case [] or ["run"]:
                return await ServiceRunner.RunAsync(service, environment, stop).ConfigureAwait(false);
            case ["version"]:
                await output.WriteLineAsync($"{service.Definition.Name} {environment.Version}").ConfigureAwait(false);
                return ServiceExitCode.Ok;
            default:
                await error.WriteLineAsync($"usage: {service.Definition.Name} [run]").ConfigureAwait(false);
                await error.WriteLineAsync($"       {service.Definition.Name} version").ConfigureAwait(false);
                await error.WriteLineAsync().ConfigureAwait(false);
                await error.WriteLineAsync("  run       connect to the Host and serve until stopped (the Host starts it so; the default)").ConfigureAwait(false);
                await error.WriteLineAsync("  version   print the version of this service").ConfigureAwait(false);
                return ServiceExitCode.Usage;
        }
    }
}
