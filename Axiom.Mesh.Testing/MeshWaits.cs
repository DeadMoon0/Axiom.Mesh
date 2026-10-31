using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Core;

namespace Axiom.Mesh.Testing;

/// <summary>
/// The conditions a mesh's waits re-check after every fact, read from the nodes' public state alone, so that an in-memory
/// mesh and a mesh of real nodes on sockets settle and connect by the same rule.
/// </summary>
public static class MeshWaits
{
    /// <summary>Whether a node holds exactly one session to a peer device and that session is established (SPEC.md §4.5).</summary>
    /// <param name="node">The node.</param>
    /// <param name="peer">The peer device.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool IsEstablishedWith(MeshNode node, DeviceId peer) =>
        node is not null && node.Peers.FirstOrDefault(p => p.Device == peer) is { State: PeerState.Established, Sessions: 1 };

    /// <summary>
    /// Whether replication has nothing left to do on these running nodes: every node idle (no command for the store, no
    /// change unhandled, no want asked or askable, every session's lanes empty), one session per peer, and between every
    /// two of them what one sent the other handled, both ways. A want that waits for a source no node offers does not
    /// count.
    /// </summary>
    /// <param name="running">The nodes that run now.</param>
    /// <returns><see langword="true"/> when settled at this instant; a wait asks three times in a row.</returns>
    public static bool IsSettled(IReadOnlyList<MeshNode> running)
    {
        var statuses = new Dictionary<DeviceId, ReplicationStatus>();
        foreach (MeshNode node in running)
        {
            if (node.Device is not { } device)
                continue;
            statuses[device] = node.Replication;
        }

        foreach (MeshNode node in running)
        {
            if (node.Device is not { } device)
                continue;
            ReplicationStatus status = statuses[device];
            if (!status.IsIdle || status.Sessions.DistinctBy(s => s.Device).Count() != status.Sessions.Count)
                return false;
            if (node.Peers.Any(p => p.Sessions > 1 || (p.Sessions == 1 && status.Sessions.All(s => s.Device != p.Device))))
                return false;
        }

        foreach ((DeviceId device, ReplicationStatus status) in statuses)
        {
            foreach (SessionTraffic session in status.Sessions)
            {
                if (!statuses.TryGetValue(session.Device, out ReplicationStatus? other))
                    continue;
                SessionTraffic? back = other.Sessions.FirstOrDefault(s => s.Device == device);
                if (back is null || back.MessagesHandled != session.MessagesSent || back.MessagesSent != session.MessagesHandled)
                    return false;
            }
        }

        return true;
    }
}
