using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Core;

/// <summary>
/// The objects this node promised with <c>Have</c> that a peer now wants: SPEC.md §11.3 forbids evicting such an object
/// while the <c>Want</c> is outstanding. A session protects a hash when the peer's <c>Want</c> for a claimed object
/// arrives, and releases it once the <c>Object</c> was sent, the peer cancelled, or the session ended. Thread-safe.
/// </summary>
internal sealed class Promises
{
    private readonly object gate = new();
    private readonly Dictionary<ObjectHash, int> protectedBy = [];
    private readonly HashSet<(DeviceId Peer, ObjectHash Hash)> outstanding = [];

    /// <summary>A peer wants an object this node claimed: it may not be evicted until released.</summary>
    /// <param name="peer">The peer.</param>
    /// <param name="hash">The object.</param>
    public void Protect(DeviceId peer, ObjectHash hash)
    {
        lock (gate)
        {
            if (outstanding.Add((peer, hash)))
                protectedBy[hash] = protectedBy.GetValueOrDefault(hash) + 1;
        }
    }

    /// <summary>The peer's <c>Want</c> for the object is answered or cancelled.</summary>
    /// <param name="peer">The peer.</param>
    /// <param name="hash">The object.</param>
    public void Release(DeviceId peer, ObjectHash hash)
    {
        lock (gate)
        {
            if (outstanding.Remove((peer, hash)))
                Decrement(hash);
        }
    }

    /// <summary>The peer's session ended: none of its <c>Want</c>s is outstanding any more.</summary>
    /// <param name="peer">The peer.</param>
    public void ReleaseAll(DeviceId peer)
    {
        lock (gate)
        {
            foreach ((DeviceId Peer, ObjectHash Hash) promise in outstanding.Where(p => p.Peer == peer))
                Decrement(promise.Hash);
            outstanding.RemoveWhere(p => p.Peer == peer);
        }
    }

    /// <summary>Whether a <c>Want</c> for the object is outstanding from any peer it was promised to.</summary>
    /// <param name="hash">The object.</param>
    /// <returns><see langword="true"/> when eviction must wait.</returns>
    public bool IsProtected(ObjectHash hash)
    {
        lock (gate)
            return protectedBy.ContainsKey(hash);
    }

    private void Decrement(ObjectHash hash)
    {
        int left = protectedBy[hash] - 1;
        if (left == 0)
            protectedBy.Remove(hash);
        else
            protectedBy[hash] = left;
    }
}
