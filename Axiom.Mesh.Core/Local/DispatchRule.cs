using System;
using System.Collections.Generic;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The checks the dispatcher makes before any handler runs (LOCAL-SPEC.md §3.4, §6 header, §2.2), in this order: the
/// role may send the request; an identity exists when the request needs one; the session is not Draining unless the
/// request is served then; a user-key request comes from a listed executable when the configuration confines them; and
/// this Host serves the request at all.
/// </summary>
internal static class DispatchRule
{
    /// <summary>Decides whether a request reaches its handler.</summary>
    /// <param name="route">The request's row.</param>
    /// <param name="role">The connection's role, fixed at Ready.</param>
    /// <param name="hasIdentity">Whether the device has an identity.</param>
    /// <param name="draining">Whether the session is Draining.</param>
    /// <param name="executablePermitted">Whether the connection's executable may send user-key requests.</param>
    /// <returns><see cref="Status.Ok"/> to proceed; else the status and the explanation to answer with.</returns>
    public static (Status Status, string Message) Check(RequestRoute route, Role role, bool hasIdentity, bool draining, bool executablePermitted)
    {
        ArgumentNullException.ThrowIfNull(route);
        RequestDescriptor request = route.Descriptor;
        if (!request.Allows(role))
            return (Status.NotPermitted, $"{request.Name} is not a request the {RoleName(role)} role may send (LOCAL-SPEC.md §{request.Section}).");
        if (route.NeedsIdentity && !hasIdentity)
            return (Status.NoIdentity, $"{request.Name} needs an identity, and this device has none yet: create one, or enrol it with a device that has one.");
        if (draining && !route.ServedWhileDraining)
            return (Status.Draining, "The Host is stopping; finish what you can and close the connection.");
        if (route.UserKey && !executablePermitted)
            return (Status.NotPermitted, $"{request.Name} spends the user key, and the Host's configuration confines that to listed executables (userKeyRequests.confineTo).");
        if (route.Handler is null)
            return (Status.UnknownRequest, $"This Host does not serve {request.Name} yet.");
        return (Status.Ok, string.Empty);
    }

    /// <summary>Whether a connection's executable may send user-key requests (§2.2): any, when nothing is listed.</summary>
    /// <param name="executable">The connection's executable; empty when the platform did not say.</param>
    /// <param name="confineTo">The listed executables.</param>
    /// <param name="comparison">How paths compare on this platform.</param>
    /// <returns><see langword="true"/> when permitted.</returns>
    public static bool Permits(string executable, IReadOnlyList<string> confineTo, StringComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(confineTo);
        if (confineTo.Count == 0)
            return true;
        if (string.IsNullOrEmpty(executable))
            return false;
        foreach (string listed in confineTo)
        {
            if (string.Equals(listed, executable, comparison))
                return true;
        }

        return false;
    }

    private static string RoleName(Role role) => role switch
    {
        Role.User => "user",
        Role.Service => "service",
        _ => "unknown",
    };
}
