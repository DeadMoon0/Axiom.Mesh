using System;

namespace Axiom.Mesh.Core;

/// <summary>
/// Stores an object: the only way into the store, and it takes nothing but a <see cref="StorableObject"/> (ARCHITECTURE.md
/// rule 4). The object file is written before the batch's transaction; the index rows, the store sequence of an Entry,
/// the Head rule, references (from the envelope), pins and the redaction flag in it.
/// </summary>
internal sealed class AdmitCommand : StoreCommand<AdmitResult>
{
    private IDisposable? lease;

    /// <summary>An object to store.</summary>
    /// <param name="storable">The object.</param>
    /// <param name="origin">Who sent or wrote it.</param>
    /// <param name="context">The Package it was wanted or written for, when known.</param>
    /// <param name="lease">The pooled buffer the object's bytes live in, given back once the command is done; or <see langword="null"/>.</param>
    public AdmitCommand(StorableObject storable, Origin origin, PackageId? context, IDisposable? lease)
    {
        ArgumentNullException.ThrowIfNull(storable);
        Object = storable;
        Origin = origin;
        Context = context;
        this.lease = lease;
    }

    /// <summary>The object.</summary>
    public StorableObject Object { get; }

    /// <summary>Who sent or wrote it.</summary>
    public Origin Origin { get; }

    /// <summary>The Package it was wanted or written for, when known; attribution only.</summary>
    public PackageId? Context { get; }

    /// <inheritdoc />
    public override long Bytes => Object.Bytes.Length;

    /// <inheritdoc />
    public override void Release()
    {
        lease?.Dispose();
        lease = null;
    }
}
