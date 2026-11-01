using System;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The configured services with the tokens of the spawned ones as they stand now (LOCAL-SPEC.md §4.2, decision 27): the
/// configuration names a spawned service, the Host's supervisor sets its token for each child it starts and withdraws it
/// when the child exits. <see cref="Current"/> is what <c>Hello</c> is checked against; a withdrawn token is
/// <see cref="ServiceToken.None"/>, which proves nothing.
/// </summary>
internal sealed class ServiceDirectory
{
    private readonly object gate = new();
    private IReadOnlyList<LocalService> current;

    /// <summary>The directory of a configuration; a spawned service starts with the token its configuration states, usually none.</summary>
    /// <param name="configured">The configured services.</param>
    public ServiceDirectory(IReadOnlyList<LocalService> configured) => current = configured ?? throw new ArgumentNullException(nameof(configured));

    /// <summary>The services with their tokens now.</summary>
    public IReadOnlyList<LocalService> Current
    {
        get
        {
            lock (gate)
                return current;
        }
    }

    /// <summary>Sets a spawned service's token: its next child presents it.</summary>
    /// <param name="name">The service's name.</param>
    /// <param name="token">The child's token; never <see cref="ServiceToken.None"/>.</param>
    /// <exception cref="ArgumentException">The name is not a configured spawned service, or the token is none.</exception>
    public void Set(string name, ServiceToken token)
    {
        if (token.IsNone)
            throw new ArgumentException("A spawned service's token is 32 random bytes, never all zero (LOCAL-SPEC.md §4.2); withdraw a token with ClearSpawnedToken.", nameof(token));
        Replace(name, token);
    }

    /// <summary>Withdraws a spawned service's token: no connection proves the name until the next one is set.</summary>
    /// <param name="name">The service's name.</param>
    /// <exception cref="ArgumentException">The name is not a configured spawned service.</exception>
    public void Clear(string name) => Replace(name, ServiceToken.None);

    private void Replace(string name, ServiceToken token)
    {
        lock (gate)
        {
            LocalService? service = current.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
            if (service is not { Provenance: ServiceProvenance.Spawned })
                throw new ArgumentException($"'{name}' is not a spawned service of this Host's configuration; only a spawned service's token is set by the Host (LOCAL-SPEC.md §4.2).", nameof(name));
            current = [.. current.Select(s => ReferenceEquals(s, service) ? s with { Token = token } : s)];
        }
    }
}
