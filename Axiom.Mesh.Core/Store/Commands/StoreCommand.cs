using System;

namespace Axiom.Mesh.Core;

/// <summary>
/// One change for the single store writer (plan decision D8). A command is applied in a batch with up to 255 others in
/// one database transaction, and completes only after that transaction committed, so an awaited command has taken
/// effect and a failed one has none.
/// </summary>
internal abstract class StoreCommand
{
    /// <summary>The object bytes the command carries, counted against the batch's 8 MiB.</summary>
    public virtual long Bytes => 0;

    /// <summary>Fails the command; it had no effect.</summary>
    /// <param name="error">Why.</param>
    public abstract void Fail(Exception error);

    /// <summary>Gives back what the command borrowed (a pooled buffer); called once, after it completed or failed.</summary>
    public virtual void Release()
    {
    }
}
