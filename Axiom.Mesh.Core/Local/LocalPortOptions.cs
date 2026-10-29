using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Core;

/// <summary>
/// What the local port is told by the Host's configuration (LOCAL-SPEC.md §2.2, §4.2, §4.5, §13): the services it
/// lists, the executables the user-key requests are confined to, the user the Host serves, and the drain's grace.
/// Every value has a documented default.
/// </summary>
public sealed record LocalPortOptions
{
    /// <summary>The documented defaults: no services, no confinement, a grace of 5 seconds.</summary>
    public static LocalPortOptions Default { get; } = new();

    /// <summary>The configured services, each name once. Default none.</summary>
    public IReadOnlyList<LocalService> Services { get; init; } = [];

    /// <summary>
    /// The executables (absolute paths) that may send the user-key requests of §2.2; empty lets every connection send
    /// them. Each such request is logged with the connection's process id and executable either way. Default empty.
    /// </summary>
    public IReadOnlyList<string> ConfineUserKeyRequestsTo { get; init; } = [];

    /// <summary>
    /// The operating-system user the Host runs as, named as <see cref="ProcessIdentity.User"/> names users: a connection
    /// of another user gets Host capability bit 1 (§14.4). Empty when not known. Default empty.
    /// </summary>
    public string HostUser { get; init; } = string.Empty;

    /// <summary>What <c>HelloAck.hostVersion</c> says, for logs. Default <c>axiom-mesh</c>.</summary>
    public string HostVersion { get; init; } = "axiom-mesh";

    /// <summary>How long a stopping node lets its local clients finish after <c>Drain</c> (§4.5): 1 ms to 30 s. Default 5 s.</summary>
    public TimeSpan DrainGrace { get; init; } = TimeSpan.FromMilliseconds(Limits.DrainGraceDefaultMilliseconds);
}
