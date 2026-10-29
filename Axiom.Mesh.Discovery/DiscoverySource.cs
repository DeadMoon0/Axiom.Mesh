using System;

namespace Axiom.Mesh.Discovery;

/// <summary>
/// Which mechanism of SPEC.md §3 reported an address. Flags, because a cache records every mechanism that reported the
/// same claim.
/// </summary>
[Flags]
public enum DiscoverySource
{
    /// <summary>No mechanism: the value of an uninitialised field, never reported by a shell.</summary>
    None = 0,

    /// <summary>A DNS-SD record received over mDNS (SPEC.md §3.1).</summary>
    Mdns = 1,

    /// <summary>A datagram received on the broadcast port 41811 (SPEC.md §3.2).</summary>
    Broadcast = 2,

    /// <summary>An address the user configured (SPEC.md §3.3); it claims no ids.</summary>
    Static = 4,
}
