using System;

namespace Axiom.Mesh.Local;

/// <summary>What a client says about itself in its <c>Hello</c> (LOCAL-SPEC.md §4.2), and the clock its timeouts run on.</summary>
public sealed record LocalClientOptions
{
    /// <summary>The role to ask for; the user role unless this program is a configured service.</summary>
    public Role Role { get; init; } = Role.User;

    /// <summary>
    /// The user role: this client's name, for the Host's logs. The service role: the service's name as the Host's
    /// configuration lists it, at most 32 bytes.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>This program's version, for logs; never interpreted.</summary>
    public string ClientVersion { get; init; } = "";

    /// <summary>The client flags (§14.4).</summary>
    public ClientCapabilities Capabilities { get; init; }

    /// <summary>
    /// A spawned service's token from <see cref="ServiceToken.EnvironmentVariable"/>, or an external service's token from
    /// the Host's configuration; <see cref="ServiceToken.None"/> for a user client.
    /// </summary>
    public ServiceToken Token { get; init; }

    /// <summary>The clock the connection's timeouts run on; replace it in tests.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
