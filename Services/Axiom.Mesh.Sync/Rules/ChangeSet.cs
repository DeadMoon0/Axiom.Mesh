using System.Collections.Generic;

namespace Axiom.Mesh.Sync;

/// <summary>What a scan changed against the fingerprints (<see cref="ChangeDetection.Of"/>).</summary>
/// <param name="Reused">Files unchanged since they were read: their fingerprints stand.</param>
/// <param name="ToHash">Files new or changed, quiet long enough to read now.</param>
/// <param name="Deleted">Paths with a fingerprint that the scan did not find.</param>
/// <param name="Busy">Files changed so recently they may still be written: read at a later run; a fingerprint they have stands meanwhile.</param>
internal sealed record ChangeSet(IReadOnlyList<Fingerprint> Reused, IReadOnlyList<ScannedFile> ToHash, IReadOnlyList<string> Deleted, IReadOnlyList<ScannedFile> Busy)
{
    /// <summary>Whether the scan found nothing new, changed, deleted or busy.</summary>
    public bool Unchanged => ToHash.Count == 0 && Deleted.Count == 0 && Busy.Count == 0;
}
