using System;
using System.IO;

namespace Axiom.Mesh.Core;

/// <summary>
/// A stored object opened for reading: its length and a read-only stream over its bytes. The file was opened so that
/// eviction can unlink it meanwhile; this handle keeps the bytes until it is disposed (LOCAL-SPEC.md §9.7).
/// </summary>
internal sealed class ObjectHandle : IDisposable
{
    /// <summary>An opened object.</summary>
    /// <param name="hash">The object's hash.</param>
    /// <param name="stream">The read-only stream, positioned at the first byte; the handle owns it.</param>
    public ObjectHandle(ObjectHash hash, Stream stream)
    {
        Hash = hash;
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        Length = stream.Length;
    }

    /// <summary>The object's hash.</summary>
    public ObjectHash Hash { get; }

    /// <summary>The object's length in bytes.</summary>
    public long Length { get; }

    /// <summary>The bytes.</summary>
    public Stream Stream { get; }

    /// <inheritdoc />
    public void Dispose() => Stream.Dispose();
}
