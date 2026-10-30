using System;
using Axiom.Mesh.Core;

namespace Axiom.Mesh.Host;

/// <summary>What a daemon is started with: where it lives, what it reads from its environment, its clock and randomness.</summary>
internal sealed record HostStart
{
    /// <summary>Where it keeps everything.</summary>
    public required HostPaths Paths { get; init; }

    /// <summary>Reads an environment variable (<c>AXIOM_MESH_PIPE</c>, <c>XDG_RUNTIME_DIR</c>, …).</summary>
    public required Func<string, string?> Environment { get; init; }

    /// <summary>Its version, for logs and <c>HelloAck</c>.</summary>
    public required string Version { get; init; }

    /// <summary>Its clock. Default the system's.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Its randomness. Default the system's.</summary>
    public IEntropy Entropy { get; init; } = new SystemEntropy();

    /// <summary>Whether it also logs to the console. Default true.</summary>
    public bool ConsoleLog { get; init; } = true;
}
