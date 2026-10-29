namespace Axiom.Mesh.Local;

/// <summary>
/// What a client that keeps an index of Entries does after connecting (LOCAL-SPEC.md §9.3): store sequences mean
/// something only within one store epoch, so an index built under another epoch is discarded and rebuilt from store
/// sequence 1; otherwise the client resumes one past what it indexed.
/// </summary>
public sealed record IndexCatchUp
{
    /// <summary>Whether the index must be discarded before catching up.</summary>
    public required bool DiscardIndex { get; init; }

    /// <summary>The store sequence to subscribe or page from (<c>Filter.FromSeq</c>, <c>ListStored.FromSeq</c>).</summary>
    public required ulong FromSeq { get; init; }

    /// <summary>Decides how to catch up.</summary>
    /// <param name="indexedEpoch">The store epoch the index was built under; zero when there is no index.</param>
    /// <param name="lastIndexedSeq">The store sequence of the last Entry indexed; 0 when none.</param>
    /// <param name="hostEpoch">The Host's store epoch, from <c>HelloAck.StoreEpoch</c> or <c>RegisterKind</c>.</param>
    /// <returns>Discard and start at 1 when the epochs differ; else resume one past the last indexed.</returns>
    public static IndexCatchUp Plan(Hash32 indexedEpoch, ulong lastIndexedSeq, Hash32 hostEpoch) =>
        indexedEpoch == hostEpoch
            ? new IndexCatchUp { DiscardIndex = false, FromSeq = lastIndexedSeq + 1 }
            : new IndexCatchUp { DiscardIndex = lastIndexedSeq > 0, FromSeq = 1 };
}
