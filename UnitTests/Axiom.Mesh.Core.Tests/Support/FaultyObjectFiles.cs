using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Storage;

namespace Axiom.Mesh.Core.Tests.Support;

/// <summary>
/// Object files that fail on request: a write that dies part-way (as a crash or a full disk would leave it), and a
/// delete that a reader without delete sharing blocks. Everything else goes to the files it wraps.
/// </summary>
internal sealed class FaultyObjectFiles(IObjectFiles inner) : IObjectFiles
{
    private readonly ConcurrentDictionary<ObjectHash, bool> failingWrites = new();
    private readonly ConcurrentDictionary<ObjectHash, bool> heldByReaders = new();

    /// <inheritdoc />
    public string? StorePath => inner.StorePath;

    /// <summary>The next write of this object fails part-way.</summary>
    public void FailWriteOf(ObjectHash hash) => failingWrites[hash] = true;

    /// <summary>A reader holds the object without allowing deletion until <see cref="Release"/>.</summary>
    public void Hold(ObjectHash hash) => heldByReaders[hash] = true;

    /// <summary>The reader let go.</summary>
    public void Release(ObjectHash hash) => heldByReaders.TryRemove(hash, out _);

    /// <inheritdoc />
    public ValueTask<bool> WriteAtomicAsync(ObjectHash hash, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        failingWrites.TryRemove(hash, out _)
            ? throw new IOException("There is not enough space on the disk (injected).")
            : inner.WriteAtomicAsync(hash, bytes, cancellationToken);

    /// <inheritdoc />
    public bool TryOpenRead(ObjectHash hash, [NotNullWhen(true)] out Stream? stream) => inner.TryOpenRead(hash, out stream);

    /// <inheritdoc />
    public ObjectDeletion TryDelete(ObjectHash hash) => heldByReaders.ContainsKey(hash) ? ObjectDeletion.InUse : inner.TryDelete(hash);
}
