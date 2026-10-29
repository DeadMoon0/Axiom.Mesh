using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// Object files held in memory, for tests and in-memory meshes. One instance outlives a node's restart the way a
/// directory does: hand the same instance to the restarted node. A reader keeps the bytes it opened after a delete,
/// as on disk.
/// </summary>
public sealed class InMemoryObjectFiles : IObjectFiles
{
    private readonly ConcurrentDictionary<ObjectHash, byte[]> files = new();

    /// <inheritdoc />
    public string? StorePath => null;

    /// <summary>The number of objects held.</summary>
    public int Count => files.Count;

    /// <summary>Whether an object is held under <paramref name="hash"/>.</summary>
    /// <param name="hash">The object's hash.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool Contains(ObjectHash hash) => files.ContainsKey(hash);

    /// <inheritdoc />
    public ValueTask<bool> WriteAtomicAsync(ObjectHash hash, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(!files.ContainsKey(hash) && files.TryAdd(hash, bytes.ToArray()));
    }

    /// <inheritdoc />
    public bool TryOpenRead(ObjectHash hash, [NotNullWhen(true)] out Stream? stream)
    {
        stream = files.TryGetValue(hash, out byte[]? bytes) ? new MemoryStream(bytes, writable: false) : null;
        return stream is not null;
    }

    /// <inheritdoc />
    public ObjectDeletion TryDelete(ObjectHash hash) => files.TryRemove(hash, out _) ? ObjectDeletion.Deleted : ObjectDeletion.Absent;
}
