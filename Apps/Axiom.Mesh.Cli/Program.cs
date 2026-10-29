using System;
using System.Reflection;

namespace Axiom.Mesh.Cli;

/// <summary>The process shell: reads what only a process has and hands it to <see cref="CliProgram"/>.</summary>
internal static class Program
{
    private static int Main(string[] args) =>
        (int)CliProgram.Run(args, Console.Out, Console.Error, InformationalVersion());

    private static string InformationalVersion() =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Program).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
