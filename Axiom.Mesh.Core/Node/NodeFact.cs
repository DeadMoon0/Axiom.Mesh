using System;
using System.Net;

namespace Axiom.Mesh.Core;

/// <summary>
/// One thing that happened to a connection or a peer of a node: what the node logs, and what a test waits for instead
/// of sleeping. Facts carry no secret and nothing a peer said beyond its ids.
/// </summary>
public sealed class NodeFact
{
    /// <summary>The fact's place in the node's sequence: 1 for the first, then +1.</summary>
    public required long Sequence { get; init; }

    /// <summary>When it was recorded, on the node's clock.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>What happened.</summary>
    public required NodeFactKind Kind { get; init; }

    /// <summary>The connection's number in the node's server; 0 when the fact concerns no connection.</summary>
    public long Connection { get; init; }

    /// <summary>The peer's address, when known.</summary>
    public IPEndPoint? Remote { get; init; }

    /// <summary>The peer's device, when known; zero otherwise.</summary>
    public DeviceId Device { get; init; }

    /// <summary>What it is about besides the peer: an object's hash, or zero.</summary>
    public Hash32 Subject { get; init; }

    /// <summary>More about it, for logs; never interpreted.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <inheritdoc />
    public override string ToString()
    {
        string device = Device.Value.IsZero ? string.Empty : " device " + Device.ToString()[..16];
        string remote = Remote is null ? string.Empty : " " + Remote;
        string connection = Connection == 0 ? string.Empty : " #" + Connection;
        string subject = Subject.IsZero ? string.Empty : " " + Subject.ToHex()[..16];
        string detail = Detail.Length == 0 ? string.Empty : ": " + Detail;
        return $"{Sequence} {Kind}{connection}{remote}{device}{subject}{detail}";
    }
}
