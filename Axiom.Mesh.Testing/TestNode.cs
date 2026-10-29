using System;
using System.Net;
using Axiom.Mesh.Core;

namespace Axiom.Mesh.Testing;

/// <summary>One node of a <see cref="TestMesh"/>: its published identity, its address, what it keeps across restarts, and the running node.</summary>
public sealed class TestNode
{
    private MeshNode? node;

    internal TestNode(TestIdentity identity, IPEndPoint address, TestNodeEnvironment environment, FakeDiscovery discovery, InMemoryDialer dialer)
    {
        Identity = identity;
        Address = address;
        Environment = environment;
        Discovery = discovery;
        Dialer = dialer;
    }

    /// <summary>The node's name: its published device name.</summary>
    public string Name => Identity.Name;

    /// <summary>The published device and user it runs as.</summary>
    public TestIdentity Identity { get; }

    /// <summary>Its mesh endpoint on the in-memory network.</summary>
    public IPEndPoint Address { get; }

    /// <summary>What it keeps across restarts.</summary>
    public TestNodeEnvironment Environment { get; }

    /// <summary>Its discovery on the network's board.</summary>
    public FakeDiscovery Discovery { get; }

    /// <summary>Whether the node runs now.</summary>
    public bool IsRunning => node is not null;

    /// <summary>The running node.</summary>
    /// <exception cref="InvalidOperationException">The node is stopped.</exception>
    public MeshNode Node => node ?? throw new InvalidOperationException($"{Name} is stopped; start it with TestMesh.StartNodeAsync.");

    /// <summary>Its device id.</summary>
    public DeviceId Device => Identity.DeviceId;

    /// <summary>Its user id.</summary>
    public UserId User => Identity.UserId;

    internal InMemoryDialer Dialer { get; }

    internal void Running(MeshNode? running) => node = running;

    /// <inheritdoc />
    public override string ToString() => $"{Name} at {Address}";
}
