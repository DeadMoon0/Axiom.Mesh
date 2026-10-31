using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Cli;

/// <summary>
/// Everything a command reads from outside the command line (fundamentals plan §2.7): the connection to the Host, the
/// Host's configuration, the terminal, the clock, the files a command publishes or writes. The process shell fills it
/// with the pipe, the disk and the console; a test with an in-memory node and a fake clock.
/// </summary>
public sealed class CliEnvironment
{
    /// <summary>The tool's informational version.</summary>
    public required string Version { get; init; }

    /// <summary>
    /// Opens a new connection to the Host: a duplex stream on its pipe. Throws when nothing answers; the command line then
    /// exits 2 and names <see cref="HostAddress"/>.
    /// </summary>
    public required Func<CancellationToken, Task<Stream>> ConnectHost { get; init; }

    /// <summary>Where the Host is expected, for the line that says it is not reachable: the pipe's path.</summary>
    public required string HostAddress { get; init; }

    /// <summary>Reads the Host's <c>config.json</c>; <see langword="null"/> when there is none. The <c>cli</c> service's token lives in it.</summary>
    public Func<string?> ReadConfig { get; init; } = static () => null;

    /// <summary>Where <see cref="ReadConfig"/> reads from, for the line that says the token is missing.</summary>
    public string ConfigFile { get; init; } = "config.json";

    /// <summary>Reads an environment variable: <c>AXIOM_MESH_SERVICE_TOKEN</c> overrides the configured token.</summary>
    public Func<string, string?> Variables { get; init; } = static _ => null;

    /// <summary>Whether standard output is a terminal: a pairing code is then also printed in a box.</summary>
    public bool OutputIsTerminal { get; init; }

    /// <summary>Whether standard error is a terminal: long operations then show their progress on it.</summary>
    public bool ErrorIsTerminal { get; init; }

    /// <summary>The clock request deadlines run on.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>How long a request may wait for the Host's answer before the command exits 4. Default 30 seconds.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The directory relative paths are resolved against.</summary>
    public string CurrentDirectory { get; init; } = Directory.GetCurrentDirectory();

    /// <summary>Writes a file, for <c>object --out</c>.</summary>
    public Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> WriteFile { get; init; } = CliFiles.WriteAsync;
}
