using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Cli.Tests.Support;

/// <summary>Runs the command line with no Host: what it answers before it connects, or how it explains that nothing answers.</summary>
internal static class Offline
{
    /// <summary>Where the missing Host is expected.</summary>
    public const string Pipe = @"\\.\pipe\axiom-mesh-test";

    /// <summary>An environment whose pipe never answers.</summary>
    public static CliEnvironment Environment { get; } = new()
    {
        Version = "0.1.0+abc123",
        HostAddress = Pipe,
        ConnectHost = _ => Task.FromException<Stream>(new IOException("The system cannot find the file specified.")),
    };

    /// <summary>Runs a command line.</summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The exit code and both outputs.</returns>
    public static async Task<(CliExitCode Exit, string Output, string Error)> RunAsync(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        CliExitCode exit = await CliProgram.RunAsync((IReadOnlyList<string>)args, output, error, Environment, CancellationToken.None);
        return (exit, output.ToString().ReplaceLineEndings("\n"), error.ToString().ReplaceLineEndings("\n"));
    }
}
