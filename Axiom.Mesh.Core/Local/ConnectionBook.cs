using System;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// Every local connection of the port and where it stands (LOCAL-SPEC.md §2.3, §4.2, §12): at most 16 waiting for
/// <c>Hello</c> (a 17th closes the one that waited longest), at most 64 user connections, one connection per service
/// name. The <c>Hello</c> decision and the registration it leads to are one step under one lock, so two connections
/// cannot both take the last place or the same name.
/// </summary>
internal sealed class ConnectionBook
{
    private readonly object gate = new();
    private readonly LinkedList<LocalConnection> waiting = new();
    private readonly Dictionary<string, LocalConnection> services = new(StringComparer.Ordinal);
    private readonly HashSet<LocalConnection> users = [];

    /// <summary>Every connection past <c>Hello</c>.</summary>
    /// <returns>A copy.</returns>
    public LocalConnection[] Live()
    {
        lock (gate)
            return [.. users, .. services.Values];
    }

    /// <summary>Connections waiting for their <c>Hello</c>.</summary>
    public int Waiting
    {
        get
        {
            lock (gate)
                return waiting.Count;
        }
    }

    /// <summary>A new connection waits for its <c>Hello</c>.</summary>
    /// <param name="connection">The connection.</param>
    /// <returns>The connection that waited longest, when this one is the 17th; it is to be closed without a frame.</returns>
    public LocalConnection? Arrived(LocalConnection connection)
    {
        lock (gate)
        {
            waiting.AddLast(connection);
            if (waiting.Count <= Limits.MaxConnectionsWaitingForHello)
                return null;
            LocalConnection oldest = waiting.First!.Value;
            waiting.RemoveFirst();
            return oldest;
        }
    }

    /// <summary>Decides a <c>Hello</c> by LOCAL-SPEC.md §4.2 and, when admitted, registers the connection as live.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="hello">Its Hello.</param>
    /// <param name="configured">The configured services.</param>
    /// <returns>The verdict.</returns>
    public LocalHelloVerdict Admit(LocalConnection connection, Hello hello, IReadOnlyList<LocalService> configured)
    {
        lock (gate)
        {
            waiting.Remove(connection);
            LocalHelloVerdict verdict = LocalHelloRule.Check(hello, connection.Origin, configured, services.Keys, users.Count);
            if (!verdict.IsAdmitted)
                return verdict;
            if (hello.Role == Role.Service)
                services[hello.Name] = connection;
            else
                users.Add(connection);
            return verdict;
        }
    }

    /// <summary>The live connection of a service, for <c>ListServices</c>.</summary>
    /// <param name="name">The service's name.</param>
    /// <returns>The connection, or <see langword="null"/>.</returns>
    public LocalConnection? ServiceConnection(string name)
    {
        lock (gate)
            return services.GetValueOrDefault(name);
    }

    /// <summary>A connection ended: its place, its name and its registrations are free (§2.3).</summary>
    /// <param name="connection">The connection.</param>
    public void Left(LocalConnection connection)
    {
        lock (gate)
        {
            waiting.Remove(connection);
            users.Remove(connection);
            foreach (string name in services.Where(s => ReferenceEquals(s.Value, connection)).Select(s => s.Key).ToArray())
                services.Remove(name);
        }
    }
}
