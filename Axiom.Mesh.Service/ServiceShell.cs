using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.Mesh.Local;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Service;

/// <summary>
/// The one dishonest place of a service process: it reads the real environment, the Host's configuration file and the
/// process's signals, and builds the <see cref="ServiceEnvironment"/> every other part takes as input. A service spawned by
/// the Host finds its token, the pipe and its state directory in its environment (LOCAL-SPEC.md §4.2); one run by hand
/// finds them in the Host's <c>config.json</c> and the Host's rules for paths (<see cref="ServicePaths"/>).
/// </summary>
public static class ServiceShell
{
    /// <summary>The environment of this process for a service.</summary>
    /// <param name="serviceName">The service's name, as the Host's configuration lists it.</param>
    /// <param name="version">The program's version, printed by <c>version</c>.</param>
    /// <param name="environment">Reads an environment variable; <see cref="Environment.GetEnvironmentVariable(string)"/> for the real one.</param>
    /// <returns>The environment: the pipe, the state directory's database, the configuration, the system clock and a log on standard error.</returns>
    /// <exception cref="ServiceConfigurationException">No state directory can be found from the environment.</exception>
    public static ServiceEnvironment EnvironmentOf(string serviceName, string version, Func<string, string?> environment)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceName);
        ArgumentException.ThrowIfNullOrEmpty(version);
        ArgumentNullException.ThrowIfNull(environment);
        LocalPlatform platform = OperatingSystem.IsWindows() ? LocalPlatform.Windows : OperatingSystem.IsMacOS() ? LocalPlatform.MacOS : LocalPlatform.Linux;
        string? config = ServicePaths.ConfigFile(platform, environment);
        string? text = config is not null && File.Exists(config) ? File.ReadAllText(config) : null;
        PipeLocation? pipe = Locate(ServiceConfig.TryReadPipeName(text, out string? name) ? name : null);
        string state = ServicePaths.StateDirectory(serviceName, platform, environment) ?? throw new ServiceConfigurationException(
            $"Service {serviceName} finds no state directory: neither {ServicePaths.StateVariable} nor {ServicePaths.HomeVariable} is set, nor the platform's variables (LOCALAPPDATA on Windows, HOME elsewhere).",
            $"Let the Host spawn the service (it sets {ServicePaths.StateVariable}), or set {ServicePaths.HomeVariable} to the Host's home.");
        return new ServiceEnvironment
        {
            Version = version,
            HostAddress = pipe?.Path ?? "the Host's pipe",
            ConnectHost = pipe is null
                ? _ => throw new IOException("This platform has no pipe for the Host; set AXIOM_MESH_PIPE.")
                : cancellationToken => LocalPipe.ConnectAsync(pipe, cancellationToken),
            OpenState = cancellationToken => OpenStateAsync(state, cancellationToken),
            StateDirectory = state,
            Variables = environment,
            ReadConfig = () => text,
            ConfigFile = config ?? "config.json",
            Loggers = LoggerFactory(Console.Error),
        };
    }

    /// <summary>A token cancelled by Ctrl-C and by SIGTERM (the Host's stop, a service manager's): the service says Bye and exits.</summary>
    /// <returns>The token; it lives as long as the process.</returns>
    public static CancellationToken StopSignal()
    {
        var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        try
        {
            PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                stop.Cancel();
            });

            // Kept alive as long as the source, which the console's handler keeps for the life of the process: a
            // registration that is collected stops handling the signal.
            stop.Token.Register(static state => GC.KeepAlive(state), terminate);
        }
        catch (PlatformNotSupportedException)
        {
            // Ctrl-C (and the Host's kill after its grace) remain.
        }

        return stop.Token;
    }

    /// <summary>Runs a service as this process.</summary>
    /// <param name="service">The service.</param>
    /// <param name="args">The process's arguments.</param>
    /// <returns>The exit code.</returns>
    internal static async Task<ServiceExitCode> RunAsync(IMeshService service, string[] args)
    {
        ServiceEnvironment environment;
        try
        {
            environment = EnvironmentOf(service.Definition.Name, InformationalVersion(service), Environment.GetEnvironmentVariable);
        }
        catch (ServiceConfigurationException ex)
        {
            await Console.Error.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            return ServiceExitCode.Configuration;
        }

        return await ServiceProgram.RunAsync(service, args, Console.Out, Console.Error, environment, StopSignal()).ConfigureAwait(false);
    }

    // The state directory is the user's alone; the Host creates it for a child it spawns, a service run by hand creates it.
    private static async Task<Database> OpenStateAsync(string directory, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(directory);
        else
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return await Database.OpenAsync(Path.Combine(directory, "index"), DatabaseOptions.Default, cancellationToken).ConfigureAwait(false);
    }

    private static PipeLocation? Locate(string? configuredName)
    {
        try
        {
            return LocalPipe.Locate(configuredName);
        }
        catch (MeshException)
        {
            return null;
        }
    }

    private static ILoggerFactory LoggerFactory(TextWriter error) => new LineLoggerFactory(error);

    private static string InformationalVersion(IMeshService service) =>
        service.GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? service.Definition.Version;
}
