using System;

namespace Axiom.Mesh.Local;

/// <summary>The client flags of <c>Hello.capabilities</c> (LOCAL-SPEC.md §14.4).</summary>
/// <remarks>Bits this enumeration does not name are reserved, sent as 0 and ignored when received.</remarks>
[Flags]
public enum ClientCapabilities : uint
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>Bit 0: the client wants partial telemetry samples (§10.4).</summary>
    PartialTelemetry = 1,
}
