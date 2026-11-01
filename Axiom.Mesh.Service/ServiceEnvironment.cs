using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axiom.Mesh.Service;

/// <summary>
/// Everything a service reads from outside its own code, handed in so that <see cref="ServiceProgram.RunAsync(IMeshService, System.Collections.Generic.IReadOnlyList{string}, TextWriter, TextWriter, ServiceEnvironment, CancellationToken)"/>
/// is a function of its inputs: the way to the Host, the state's database, the environment variables, the Host's
/// configuration, the clock and the loggers. <see cref="ServiceShell.EnvironmentOf"/> builds the real one; a test builds an
/// in-memory one.
/// </summary>
public sealed class ServiceEnvironment
{
    /// <summary>The program's version, printed by <c>version</c>.</summary>
    public required string Version { get; init; }

    /// <summary>Opens a connection to the Host: the pipe of <c>AXIOM_MESH_PIPE</c>, or an in-memory pair in a test.</summary>
    public required Func<CancellationToken, Task<Stream>> ConnectHost { get; init; }

    /// <summary>Where the Host is, for messages.</summary>
    public required string HostAddress { get; init; }

    /// <summary>Opens the service's database; the runtime owns and closes what it returns. Each call opens it anew on the same storage.</summary>
    public required Func<CancellationToken, Task<Database>> OpenState { get; init; }

    /// <summary>The state directory, for messages; empty for an in-memory state.</summary>
    public string StateDirectory { get; init; } = string.Empty;

    /// <summary>Reads an environment variable: <c>AXIOM_MESH_SERVICE_TOKEN</c> and the paths. Default reads none.</summary>
    public Func<string, string?> Variables { get; init; } = static _ => null;

    /// <summary>The text of the Host's <c>config.json</c>, for a service run by hand; <see langword="null"/> when there is none.</summary>
    public Func<string?> ReadConfig { get; init; } = static () => null;

    /// <summary>The configuration file, for messages.</summary>
    public string ConfigFile { get; init; } = "config.json";

    /// <summary>The clock reconnects wait on and callbacks' deadlines run on. Default the system's.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Where the runtime and the service log. Default none.</summary>
    public ILoggerFactory Loggers { get; init; } = NullLoggerFactory.Instance;
}
