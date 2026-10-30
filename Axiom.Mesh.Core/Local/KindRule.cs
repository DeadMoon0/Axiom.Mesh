using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// LOCAL-SPEC.md §9.2 <c>RegisterKind</c>, as a function of the service's configuration and the registrations held: the
/// Entry kinds are non-empty and ascending (<c>Invalid</c>); the Package kind and every Entry kind are configured for the
/// service (<c>NotPermitted</c>); a kind another service holds is <c>KindTaken</c>; an Entry kind another registered kind
/// lists is <c>Invalid</c>; a kind this service holds already is accepted and changes nothing.
/// </summary>
internal static class KindRule
{
    /// <summary>Decides a registration.</summary>
    /// <param name="service">The service's configuration.</param>
    /// <param name="kind">The Package kind.</param>
    /// <param name="entryKinds">Its Entry kinds.</param>
    /// <param name="held">Every registration now: kind → whether this service holds it, and its Entry kinds.</param>
    /// <returns><see langword="null"/> when the kind may be (or is) registered; else why not.</returns>
    public static RequestRefusal? Check(LocalService service, PackageKind kind, IReadOnlyList<EntryKind> entryKinds, IReadOnlyDictionary<PackageKind, (bool Own, IReadOnlyList<EntryKind> EntryKinds)> held)
    {
        if (entryKinds.Count == 0 || !PackageRequests.Ascending(entryKinds.Select(k => (ushort)k)))
            return new RequestRefusal(Status.Invalid, "The Entry kinds must be non-empty, ascending and unique.", "entryKinds");
        if (!service.Kinds.Contains(kind))
            return new RequestRefusal(Status.NotPermitted, $"Service \"{service.Name}\" is not configured for Package kind {(ushort)kind}.", "kind");
        foreach (EntryKind entryKind in entryKinds)
        {
            if (!service.EntryKinds.Contains(entryKind))
                return new RequestRefusal(Status.NotPermitted, $"Service \"{service.Name}\" is not configured for Entry kind {(ushort)entryKind}.", "entryKinds");
        }

        if (held.TryGetValue(kind, out var registration))
        {
            return registration.Own
                ? null
                : new RequestRefusal(Status.KindTaken, $"Another connected service holds Package kind {(ushort)kind}.", "kind");
        }

        foreach ((PackageKind other, (bool _, IReadOnlyList<EntryKind> listed)) in held)
        {
            if (entryKinds.Where(listed.Contains).Select(k => (EntryKind?)k).FirstOrDefault() is { } shared)
                return new RequestRefusal(Status.Invalid, $"Entry kind {(ushort)shared} belongs to the registered Package kind {(ushort)other}.", "entryKinds");
        }

        return null;
    }
}
