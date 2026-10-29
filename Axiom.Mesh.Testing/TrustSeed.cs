using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Testing;

/// <summary>
/// Which nodes of a test mesh know which other nodes' users when the mesh starts, as a completed pairing would leave
/// them: each such node holds the other user's key and Roster. Until pairing is built into the harness, this is how a
/// test mesh gets its trust. Every node always knows its own user.
/// </summary>
public sealed class TrustSeed
{
    private TrustSeed(IReadOnlyList<(string Node, string Knows)> pairs) => Pairs = pairs;

    /// <summary>Every node knows its own user only: no two users' devices accept each other.</summary>
    public static TrustSeed OwnUsersOnly { get; } = new([]);

    /// <summary>The ordered pairs: the node on the left holds the user of the node on the right.</summary>
    public IReadOnlyList<(string Node, string Knows)> Pairs { get; }

    /// <summary>Every listed node knows the user of every other listed node.</summary>
    /// <param name="nodes">The node names, at least two.</param>
    /// <returns>The seed.</returns>
    /// <exception cref="ArgumentException">Fewer than two nodes.</exception>
    public static TrustSeed MutuallyKnown(params string[] nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (nodes.Length < 2)
            throw new ArgumentException("Name at least two nodes that know each other.", nameof(nodes));
        return new([.. nodes.SelectMany(a => nodes.Where(b => b != a).Select(b => (a, b)))]);
    }

    /// <summary>This seed and, besides, <paramref name="node"/> knowing the user of <paramref name="knows"/>.</summary>
    /// <param name="node">The node that knows.</param>
    /// <param name="knows">The node whose user it knows.</param>
    /// <returns>The combined seed.</returns>
    public TrustSeed And(string node, string knows) => new([.. Pairs, (node, knows)]);
}
