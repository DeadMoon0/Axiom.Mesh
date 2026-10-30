using System;
using System.Collections.Generic;
using System.Net;
using Axiom.LDB;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Discovery;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Core;

/// <summary>
/// Everything dishonest a node needs, handed in (plan §2.5): where object files live, the database, the private keys,
/// discovery, how peers are dialled, the clock, randomness and logging, and whether the node binds sockets or runs in
/// memory. The Host fills it with disks, sockets and the system clock; a test with in-memory parts and a fake clock.
/// </summary>
public sealed class MeshNodeEnvironment
{
    /// <summary>Where the node's objects live (SPEC.md §11.1).</summary>
    public required IObjectFiles Files { get; init; }

    /// <summary>The node's database, opened by the caller, who disposes it after the node stopped.</summary>
    public required Database Database { get; init; }

    /// <summary>The node's private keys.</summary>
    public required IKeyStore Keys { get; init; }

    /// <summary>How the node finds peers (SPEC.md §3); the node is its one observer.</summary>
    public required IDiscovery Discovery { get; init; }

    /// <summary>How the node opens connections to peers (SPEC.md §4.1).</summary>
    public required IDialer Dialer { get; init; }

    /// <summary>The node's one clock: every deadline, backoff and timestamp of the node runs on it.</summary>
    public required TimeProvider Time { get; init; }

    /// <summary>The node's randomness.</summary>
    public required IEntropy Entropy { get; init; }

    /// <summary>Where the node logs.</summary>
    public required ILoggerFactory Loggers { get; init; }

    /// <summary>
    /// Whether the node binds no socket: its mesh port then accepts only <see cref="MeshNode.ConnectInMemory"/>, with
    /// the same admission, TLS and protocol as a socket. For in-memory meshes in tests.
    /// </summary>
    public bool InMemory { get; init; }

    /// <summary>
    /// Reads the addresses this device is reachable at on its networks, for the code a showing pairing shows (PAIR-SPEC.md
    /// §2.2); the mesh port is added to each. Default: the unicast addresses of every network interface that is up, not
    /// loopback, IPv4 first. A test mesh names its in-memory addresses.
    /// </summary>
    public Func<IReadOnlyList<IPAddress>> Addresses { get; init; } = NetworkAddresses.Read;
}
