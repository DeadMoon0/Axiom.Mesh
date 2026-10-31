using System;
using System.Threading.Tasks;
using Axiom.Mesh.Benchmarks.Scenarios;
using BenchmarkDotNet.Running;

namespace Axiom.Mesh.Benchmarks;

/// <summary>
/// Runs the micro benchmarks with BenchmarkDotNet (<c>dotnet run -c Release -- --filter *Hash*</c>), or one scenario on
/// real nodes (<c>dotnet run -c Release -- scenario walk</c>); <c>scenario</c> without a name lists them.
/// </summary>
public static class Program
{
    /// <summary>The entry point.</summary>
    /// <param name="args">BenchmarkDotNet's arguments, or <c>scenario</c> and a scenario's name.</param>
    /// <returns>0 on success.</returns>
    public static async Task<int> Main(string[] args)
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        if (args.Length > 0 && args[0] == "scenario")
            return await ScenarioRunner.RunAsync(args.AsMemory(1), Console.Out).ConfigureAwait(false);
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }
}
