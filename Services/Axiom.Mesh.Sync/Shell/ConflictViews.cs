namespace Axiom.Mesh.Sync;

/// <summary>A recorded conflict as the namespace shows it (SYNC-SERVICE.md §4). Pure.</summary>
internal static class ConflictViews
{
    /// <summary>The view of a row.</summary>
    /// <param name="row">The row.</param>
    /// <param name="directory">Its binding's directory.</param>
    /// <returns>The record.</returns>
    public static Conflict Of(ConflictRow row, string directory) => new()
    {
        Id = row.Id,
        Directory = directory,
        Path = TextCut.Of(row.Path, SyncLimits.PathLength),
        Kind = (ConflictKind)row.Kind,
        CopyPath = TextCut.Of(row.CopyPath, SyncLimits.PathLength),
        OtherDevice = new DeviceId(RowBytes.Hash(row.OtherDevice)),
        Version = new ObjectHash(RowBytes.Hash(row.Version)),
        At = (ulong)System.Math.Max(row.AtMs, 0),
    };
}
