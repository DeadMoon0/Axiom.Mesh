using System;
using System.Collections.Generic;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The checks of LOCAL-SPEC.md §4.2 on a client's <c>Hello</c>, in the order the section states them, as a function of
/// the Hello, the connection's process identity, the configured services and what is connected now. The first failure
/// decides the <c>Bye</c>.
/// </summary>
/// <remarks>
/// Check 2 (a role outside the registry) never reaches this rule: the frame codec reads such a Hello as out of its
/// bounds, and the definition closes with <c>Bye(Violation)</c> (§11.3).
/// </remarks>
internal static class LocalHelloRule
{
    /// <summary>Decides a <c>Hello</c>.</summary>
    /// <param name="hello">The client's Hello.</param>
    /// <param name="origin">Who connected: a program on the pipe, or an in-process service on its in-memory pair.</param>
    /// <param name="services">The services the configuration lists.</param>
    /// <param name="liveServiceNames">The names of service connections in Ready or Draining now.</param>
    /// <param name="liveUserConnections">User connections in Ready or Draining now.</param>
    /// <returns>Admit, or the Bye to send.</returns>
    public static LocalHelloVerdict Check(Hello hello, ProcessIdentity origin, IReadOnlyList<LocalService> services, IReadOnlyCollection<string> liveServiceNames, int liveUserConnections)
    {
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(liveServiceNames);

        // 1. The version.
        if (hello.Version != LocalProtocol.Version)
            return LocalHelloVerdict.Refuse(Bye.Of(ByeReason.Refused, "version"));

        if (hello.Role == Role.Service)
        {
            // 3. A listed name, proven: in-process only on its own pair, spawned and external by their token.
            if (!TryFind(services, hello.Name, out LocalService? service) || !Proves(hello, origin, service))
                return LocalHelloVerdict.Refuse(Bye.Of(ByeReason.Refused, "service"));

            // 4. One connection per name; the live one is kept.
            if (Contains(liveServiceNames, hello.Name))
                return LocalHelloVerdict.Refuse(Bye.Of(ByeReason.Duplicate, string.Empty));
            return LocalHelloVerdict.Admit(service);
        }

        // 5. The connection limits of §12.
        if (liveUserConnections >= Limits.MaxUserConnections)
            return LocalHelloVerdict.Refuse(Bye.Of(ByeReason.Refused, "busy"));
        return LocalHelloVerdict.Admit(null);
    }

    private static bool Proves(Hello hello, ProcessIdentity origin, LocalService service) => service.Provenance switch
    {
        ServiceProvenance.InProcess => origin.Kind == ProcessKind.InProcess && string.Equals(origin.Service, service.Name, StringComparison.Ordinal),

        // A token that was never set proves nothing, however the client's compares.
        ServiceProvenance.Spawned or ServiceProvenance.External => !service.Token.IsNone & hello.Token.FixedTimeEquals(service.Token),
        _ => false,
    };

    private static bool TryFind(IReadOnlyList<LocalService> services, string name, out LocalService service)
    {
        foreach (LocalService candidate in services)
        {
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                service = candidate;
                return true;
            }
        }

        service = null!;
        return false;
    }

    private static bool Contains(IReadOnlyCollection<string> names, string name)
    {
        foreach (string live in names)
        {
            if (string.Equals(live, name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
