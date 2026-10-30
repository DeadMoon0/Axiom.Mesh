using System.Collections.Generic;

namespace Axiom.Mesh.Host;

/// <summary><c>mesh</c>: the mesh listener and how peers are found (SPEC.md §3, §4).</summary>
internal sealed record MeshSection
{
    /// <summary>The TCP port the mesh listener binds; 0 lets the system choose. Default 41810.</summary>
    public int Port { get; set; } = 41810;

    /// <summary>The address it binds; <see langword="null"/> for every address. Default null.</summary>
    public string? Listen { get; set; }

    /// <summary>Whether the device announces itself and listens over mDNS/DNS-SD (SPEC.md §3.1). Default true.</summary>
    public bool Mdns { get; set; } = true;

    /// <summary>Whether it announces itself and listens on the broadcast port 41811 (SPEC.md §3.2). Default true.</summary>
    public bool Broadcast { get; set; } = true;

    /// <summary>Addresses to dial without discovery, <c>host:port</c> (SPEC.md §3.3). Default none.</summary>
    public List<string> StaticPeers { get; set; } = [];
}
