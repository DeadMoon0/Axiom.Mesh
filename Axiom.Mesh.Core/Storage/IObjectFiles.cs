using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// Where object bytes live: one immutable file per object, named by its hash (SPEC.md §11.1; LOCAL-SPEC.md §9.7). The
/// one seam the disk enters the store by; the index of what is stored lives in the database, never here.
/// </summary>
/// <remarks>
/// Implementations are thread-safe. Only the store's single writer writes and deletes; readers open concurrently.
/// </remarks>
public interface IObjectFiles
{
    /// <summary>
    /// The store directory that LOCAL-SPEC.md §9.7 <c>GetStorePath</c> answers, holding <c>objects/&lt;h0h1&gt;/&lt;h2h3&gt;/&lt;hash&gt;</c>;
    /// <see langword="null"/> for a store that is not on disk.
    /// </summary>
    string? StorePath { get; }

    /// <summary>
    /// Writes an object so that a crash leaves it absent or complete, never partial (SPEC.md §11.1). Idempotent: an
    /// object already present is left as it is.
    /// </summary>
    /// <param name="hash">The object's hash; the caller proved it is <c>H(bytes)</c>.</param>
    /// <param name="bytes">Every byte of the object.</param>
    /// <param name="cancellationToken">Cancels the write; nothing is left under the hash then.</param>
    /// <returns><see langword="true"/> when the object was written now; <see langword="false"/> when it was present.</returns>
    ValueTask<bool> WriteAtomicAsync(ObjectHash hash, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);

    /// <summary>
    /// Opens an object for reading so that the store can delete it meanwhile: the reader keeps the bytes, not the name
    /// (LOCAL-SPEC.md §9.7, <c>FILE_SHARE_DELETE</c> on Windows).
    /// </summary>
    /// <param name="hash">The object's hash.</param>
    /// <param name="stream">A read-only stream positioned at the first byte, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when no object is stored under the hash.</returns>
    bool TryOpenRead(ObjectHash hash, [NotNullWhen(true)] out Stream? stream);

    /// <summary>Removes an object's name. A reader that holds it open keeps reading its bytes.</summary>
    /// <param name="hash">The object's hash.</param>
    /// <returns>
    /// <see cref="ObjectDeletion.Deleted"/>, <see cref="ObjectDeletion.Absent"/> when there was nothing to delete, or
    /// <see cref="ObjectDeletion.InUse"/> when a reader that did not allow deletion holds it: evict later.
    /// </returns>
    ObjectDeletion TryDelete(ObjectHash hash);
}
