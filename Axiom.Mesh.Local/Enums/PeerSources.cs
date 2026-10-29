using System;

namespace Axiom.Mesh.Local;

/// <summary>Where this node learned of a peer, <c>Peer.sources</c> (LOCAL-SPEC.md §14.11).</summary>
/// <remarks>Bits 4 to 7 are reserved and ignored when received.</remarks>
[Flags]
public enum PeerSources : byte
{
    /// <summary>No source.</summary>
    None = 0,

    /// <summary>Bit 0: mDNS/DNS-SD.</summary>
    Mdns = 1,

    /// <summary>Bit 1: UDP broadcast.</summary>
    Broadcast = 2,

    /// <summary>Bit 2: a static address.</summary>
    Static = 4,

    /// <summary>Bit 3: an established session.</summary>
    Session = 8,
}
