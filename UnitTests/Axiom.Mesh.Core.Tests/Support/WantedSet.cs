using System.Collections.Concurrent;

namespace Axiom.Mesh.Core.Tests.Support;

/// <summary>What a test says the node asked for; everything when <see cref="All"/> is set.</summary>
internal sealed class WantedSet : IWantedSet
{
    private readonly ConcurrentDictionary<ObjectHash, bool> wanted = new();

    /// <summary>Whether every object counts as wanted.</summary>
    public bool All { get; set; }

    /// <summary>Wants an object.</summary>
    public void Want(ObjectHash hash) => wanted[hash] = true;

    /// <inheritdoc />
    public bool IsWanted(ObjectHash hash) => All || wanted.ContainsKey(hash);
}
