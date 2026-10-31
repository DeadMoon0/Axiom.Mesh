using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// The process shell: reads what only a process has (the console, the environment, the pipe, the disk, Ctrl-C) and hands
/// it to <see cref="CliProgram"/>.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var interrupt = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            interrupt.Cancel();
        };

        return (int)await CliProgram.RunAsync(args, Console.Out, Console.Error, Environment(), interrupt.Token).ConfigureAwait(false);
    }

    private static CliEnvironment Environment()
    {
        LocalPlatform platform = OperatingSystem.IsWindows() ? LocalPlatform.Windows : OperatingSystem.IsMacOS() ? LocalPlatform.MacOS : LocalPlatform.Linux;
        string? config = CliPaths.ConfigFile(platform, System.Environment.GetEnvironmentVariable);
        string? text = config is not null && File.Exists(config) ? File.ReadAllText(config) : null;
        PipeLocation? pipe = Locate(CliConfig.TryReadPipeName(text, out string? name) ? name : null);
        return new CliEnvironment
        {
            Version = InformationalVersion(),
            HostAddress = pipe?.Path ?? "the Host's pipe",
            ConnectHost = pipe is null
                ? _ => throw new IOException("This platform has no pipe for the Host.")
                : cancellationToken => LocalPipe.ConnectAsync(pipe, cancellationToken),
            ConfigFile = config ?? "config.json",
            ReadConfig = () => text,
            Variables = System.Environment.GetEnvironmentVariable,
            OutputIsTerminal = !Console.IsOutputRedirected,
            ErrorIsTerminal = !Console.IsErrorRedirected,
        };
    }

    // AXIOM_MESH_PIPE, else pipe.name of config.json, else the default: the Host's own precedence (STRUCTURAL-DEBT.md 48).
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

    private static string InformationalVersion() =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Program).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
