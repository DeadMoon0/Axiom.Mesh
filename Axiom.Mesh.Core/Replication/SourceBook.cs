using System.Collections.Generic;

namespace Axiom.Mesh.Core;

/// <summary>
/// The hashes one peer claimed with <c>Have</c> (SPEC.md §7.4), so that a want made later finds it as a source: at most
/// 65 536 per peer, the oldest dropped beyond that. A <c>NotFound</c> from the peer removes the hash until a later
/// <c>Have</c> (§10.4). Not thread-safe; the scheduler guards it.
/// </summary>
internal sealed class SourceBook
{
    private readonly int capacity;
    private readonly Dictionary<ObjectHash, LinkedListNode<ObjectHash>> index = [];
    private readonly LinkedList<ObjectHash> order = new();

    /// <summary>A book with the bound of SPEC.md §7.4.</summary>
    public SourceBook()
        : this(Limits.MaxRecordedSources)
    {
    }

    /// <summary>A book with another bound, for tests.</summary>
    /// <param name="capacity">The most hashes kept.</param>
    public SourceBook(int capacity) => this.capacity = capacity;

    /// <summary>How many hashes are recorded.</summary>
    public int Count => index.Count;

    /// <summary>Records a claim; a hash claimed again counts as new.</summary>
    /// <param name="hash">The hash.</param>
    public void Add(ObjectHash hash)
    {
        if (index.Remove(hash, out LinkedListNode<ObjectHash>? existing))
            order.Remove(existing);
        index[hash] = order.AddLast(hash);
        while (index.Count > capacity)
        {
            LinkedListNode<ObjectHash> oldest = order.First!;
            order.RemoveFirst();
            index.Remove(oldest.Value);
        }
    }

    /// <summary>Whether the peer claimed the hash and has not denied it since.</summary>
    /// <param name="hash">The hash.</param>
    /// <returns><see langword="true"/> when recorded.</returns>
    public bool Contains(ObjectHash hash) => index.ContainsKey(hash);

    /// <summary>Forgets a claim: the peer answered <c>NotFound</c>.</summary>
    /// <param name="hash">The hash.</param>
    public void Remove(ObjectHash hash)
    {
        if (index.Remove(hash, out LinkedListNode<ObjectHash>? node))
            order.Remove(node);
    }
}
