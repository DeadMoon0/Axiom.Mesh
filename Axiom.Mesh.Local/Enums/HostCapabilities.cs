using System;

namespace Axiom.Mesh.Local;

/// <summary>The Host flags of <c>HelloAck.capabilities</c> (LOCAL-SPEC.md §14.4), computed per connection.</summary>
/// <remarks>Bits this enumeration does not name are reserved and ignored when received.</remarks>
[Flags]
public enum HostCapabilities : uint
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>Bit 0: the store directory is readable by this connection's user; <c>GetStorePath</c> succeeds (§9.7).</summary>
    StoreReadable = 1,

    /// <summary>Bit 1: the Host runs under another OS account than this connection's user (§13).</summary>
    OtherAccount = 2,

    /// <summary>Bit 2: the Host keeps telemetry (§10).</summary>
    Telemetry = 4,
}
