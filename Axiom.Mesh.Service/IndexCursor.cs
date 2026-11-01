namespace Axiom.Mesh.Service;

/// <summary>
/// How far a service has indexed (LOCAL-SPEC.md §9.3): the store epoch it indexed under and the store sequence of the last
/// Entry its handler finished. Zero and 0 for a service that indexed nothing yet.
/// </summary>
public sealed record IndexCursor
{
    /// <summary>The cursor of a service that indexed nothing.</summary>
    public static IndexCursor None { get; } = new() { Epoch = default, LastSeq = 0 };

    /// <summary>The store epoch the index was built under; zero when none.</summary>
    public required Hash32 Epoch { get; init; }

    /// <summary>The store sequence of the last Entry handled; 0 when none.</summary>
    public required ulong LastSeq { get; init; }
}
