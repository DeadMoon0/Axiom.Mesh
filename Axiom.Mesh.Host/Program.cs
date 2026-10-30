using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Host;

/// <summary>The process shell: reads what only a process has (arguments, environment, signals) and hands it to <see cref="HostProgram"/>.</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, press) =>
        {
            press.Cancel = true;
            stop.Cancel();
        };
        using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, signal =>
        {
            signal.Cancel = true;
            stop.Cancel();
        });

        return (int)await HostProgram.RunAsync(args, Console.Out, Console.Error, InformationalVersion(), Environment.GetEnvironmentVariable, stop.Token).ConfigureAwait(false);
    }

    private static string InformationalVersion() =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Program).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
