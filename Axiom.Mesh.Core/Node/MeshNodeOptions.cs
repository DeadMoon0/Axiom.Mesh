using System;
using System.Net;

namespace Axiom.Mesh.Core;

/// <summary>What a node is told about itself when it starts. Every value has a documented default.</summary>
public sealed record MeshNodeOptions
{
    /// <summary>The mesh listener's default port (SPEC.md §14.9).</summary>
    public const int DefaultMeshPort = 41810;

    /// <summary>The documented defaults.</summary>
    public static MeshNodeOptions Default { get; } = new();

    /// <summary>
    /// The mesh listener's TCP port; 0 lets the system choose (read <see cref="MeshNode.MeshEndpoint"/>). In memory,
    /// the number other nodes connect to. Default 41810.
    /// </summary>
    public int MeshPort { get; init; } = DefaultMeshPort;

    /// <summary>The address the mesh listener binds; <see langword="null"/> for every address, IPv6 and IPv4. Default null.</summary>
    public IPAddress? ListenAddress { get; init; }

    /// <summary>How long a stopping node lets its sessions say <c>Bye</c> before it aborts what is left. Default 5 seconds.</summary>
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
