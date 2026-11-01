using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Core;

/// <summary>
/// Which connected service holds which Package kind and its Entry kinds (LOCAL-SPEC.md §9.2): a kind is held by at most one
/// connection, while it lasts; <c>WriteEntry</c> and <c>ListStored</c> are scoped by it. The decision and the registration
/// it leads to are one step under one lock, so two services cannot both take a kind.
/// </summary>
internal sealed class KindRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<PackageKind, (LocalConnection Holder, EntryKind[] EntryKinds)> held = [];

    /// <summary>Registers a kind for a connection in the service role, as <see cref="KindRule"/> decides.</summary>
    /// <param name="connection">The service's connection.</param>
    /// <param name="service">Its configuration.</param>
    /// <param name="kind">The Package kind.</param>
    /// <param name="entryKinds">Its Entry kinds.</param>
    /// <returns><see langword="null"/> when registered (now or before); else why not.</returns>
    public RequestRefusal? TryRegister(LocalConnection connection, LocalService service, PackageKind kind, IReadOnlyList<EntryKind> entryKinds)
    {
        lock (gate)
        {
            var view = held.ToDictionary(h => h.Key, h => (ReferenceEquals(h.Value.Holder, connection), (IReadOnlyList<EntryKind>)h.Value.EntryKinds));
            if (KindRule.Check(service, kind, entryKinds, view) is { } refusal)
                return refusal;
            held.TryAdd(kind, (connection, [.. entryKinds]));
            return null;
        }
    }

    /// <summary>Whether a connection holds a Package kind and, with it, an Entry kind (§9.4 <c>KindNotRegistered</c>).</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="kind">The Package kind.</param>
    /// <param name="entryKind">The Entry kind.</param>
    /// <returns><see langword="true"/> when both are registered by it.</returns>
    public bool Holds(LocalConnection connection, PackageKind kind, EntryKind entryKind)
    {
        lock (gate)
            return held.TryGetValue(kind, out var registration) && ReferenceEquals(registration.Holder, connection) && registration.EntryKinds.Contains(entryKind);
    }

    /// <summary>Whether a connection holds a Package kind (§9.10 <c>Emit</c>: <c>KindNotRegistered</c> otherwise).</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="kind">The Package kind.</param>
    /// <returns><see langword="true"/> when it registered the kind.</returns>
    public bool HoldsKind(LocalConnection connection, PackageKind kind)
    {
        lock (gate)
            return held.TryGetValue(kind, out var registration) && ReferenceEquals(registration.Holder, connection);
    }

    /// <summary>The Entry kinds of every kind a connection holds (§6.4 <c>ListStored</c> in the service role).</summary>
    /// <param name="connection">The connection.</param>
    /// <returns>The Entry kinds; empty when it holds none.</returns>
    public IReadOnlySet<EntryKind> EntryKindsOf(LocalConnection connection)
    {
        lock (gate)
            return held.Values.Where(h => ReferenceEquals(h.Holder, connection)).SelectMany(h => h.EntryKinds).ToHashSet();
    }

    /// <summary>The connection ended: every kind it held is free (§9.2).</summary>
    /// <param name="connection">The connection.</param>
    public void Release(LocalConnection connection)
    {
        lock (gate)
        {
            foreach (PackageKind kind in held.Where(h => ReferenceEquals(h.Value.Holder, connection)).Select(h => h.Key).ToArray())
                held.Remove(kind);
        }
    }
}
