using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Testing;

/// <summary>What a <see cref="TestMesh"/> starts with: its nodes, their trust, and the seed of their randomness.</summary>
public sealed record TestMeshOptions
{
    /// <summary>The nodes, by published device name (<see cref="TestIdentity"/>), each at most once.</summary>
    public required IReadOnlyList<string> Nodes { get; init; }

    /// <summary>Which nodes know which other nodes' users. Default: each knows its own user only.</summary>
    public TrustSeed Trust { get; init; } = TrustSeed.OwnUsersOnly;

    /// <summary>
    /// The nodes that do not announce themselves when they start (<see cref="FakeDiscovery.IsSilent"/>): they dial the
    /// nodes they hear of but are dialled by none, so a test knows who dialled. Default none.
    /// </summary>
    public IReadOnlyList<string> Silent { get; init; } = [];

    /// <summary>
    /// Pairs of nodes that cannot reach each other, from the start (<see cref="TestMesh.Sever"/>): each still reaches every
    /// other node. Default none.
    /// </summary>
    public IReadOnlyList<(string A, string B)> Severed { get; init; } = [];

    /// <summary>The seed of every node's randomness: the same seed gives the same store epochs and names. Default 1.</summary>
    public ulong Seed { get; init; } = 1;

    /// <summary>A mesh of the named nodes with the defaults.</summary>
    /// <param name="nodes">Published device names, for example <c>alice-laptop</c> and <c>bob-desktop</c>.</param>
    /// <returns>The options.</returns>
    public static TestMeshOptions Of(params string[] nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return new TestMeshOptions { Nodes = nodes };
    }
}
