using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;

namespace Axiom.Mesh.Core;

/// <summary>
/// The object store as its users see it: put a <see cref="StorableObject"/> (nothing else is accepted), open a stored
/// object for reading, ask whether one is stored, evict one. Writes go through the single <see cref="StoreWriter"/>;
/// reads open the file directly and count as a use for least-recently-used eviction.
/// </summary>
internal sealed class ObjectStore
{
    private readonly StoreWriter writer;
    private readonly IObjectFiles files;

    /// <summary>The store over a writer and its object files.</summary>
    /// <param name="writer">The store writer.</param>
    /// <param name="files">The object files the writer writes.</param>
    public ObjectStore(StoreWriter writer, IObjectFiles files)
    {
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.files = files ?? throw new ArgumentNullException(nameof(files));
    }

    /// <summary>The objects an outstanding <c>Want</c> protects from eviction (SPEC.md §11.3).</summary>
    public Promises Promises => writer.Promises;

    /// <summary>Stores an object written on this node, idempotently.</summary>
    /// <param name="storable">The object.</param>
    /// <param name="cancellationToken">Cancels waiting for room in the writer's queue.</param>
    /// <returns>What the writer did with it.</returns>
    public Task<AdmitResult> PutAsync(StorableObject storable, CancellationToken cancellationToken) =>
        PutAsync(storable, Origin.Local, null, cancellationToken);

    /// <summary>Stores an object, idempotently.</summary>
    /// <param name="storable">The object.</param>
    /// <param name="origin">Who sent or wrote it.</param>
    /// <param name="context">The Package it was wanted or written for, when known.</param>
    /// <param name="cancellationToken">Cancels waiting for room in the writer's queue.</param>
    /// <returns>What the writer did with it.</returns>
    public Task<AdmitResult> PutAsync(StorableObject storable, Origin origin, PackageId? context, CancellationToken cancellationToken) =>
        writer.SendAsync(new AdmitCommand(storable, origin, context, null), cancellationToken);

    /// <summary>Opens a stored object for reading; counts as a use (SPEC.md §11.3).</summary>
    /// <param name="hash">The object's hash.</param>
    /// <param name="handle">The opened object, or <see langword="null"/>; dispose it.</param>
    /// <returns><see langword="false"/> when the object is not stored (never, or evicted since).</returns>
    public bool TryOpen(ObjectHash hash, [NotNullWhen(true)] out ObjectHandle? handle)
    {
        if (!files.TryOpenRead(hash, out Stream? stream))
        {
            handle = null;
            return false;
        }

        writer.TryPost(new TouchCommand(hash));
        handle = new ObjectHandle(hash, stream);
        return true;
    }

    /// <summary>Whether the index holds the object as stored, as of the last committed batch.</summary>
    /// <param name="hash">The object's hash.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns><see langword="true"/> when stored.</returns>
    public async Task<bool> ContainsAsync(ObjectHash hash, CancellationToken cancellationToken)
    {
        await using LdbContext context = await LdbContext.OpenAsync(writer.Database, cancellationToken).ConfigureAwait(false);
        return await context.Set<ObjectRow>().ReadOnlyQuery()
            .Where(ObjectRow.Columns.Hash == RowKeys.Of(hash.Value))
            .AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Evicts an object unless a pin holds it or a peer's outstanding <c>Want</c> protects it (SPEC.md §11.3). A delete a
    /// reader blocks leaves it stored, to be evicted later; it counts as evicted only once unlinked (LOCAL-SPEC.md §9.7).
    /// </summary>
    /// <param name="hash">The object's hash.</param>
    /// <param name="cancellationToken">Cancels waiting for room in the writer's queue.</param>
    /// <returns>What became of it.</returns>
    public Task<EvictionOutcome> TryEvictAsync(ObjectHash hash, CancellationToken cancellationToken) =>
        writer.SendAsync(new EvictCommand(hash), cancellationToken);
}
