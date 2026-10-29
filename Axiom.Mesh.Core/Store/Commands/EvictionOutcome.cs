namespace Axiom.Mesh.Core;

/// <summary>What became of an object the store tried to evict (SPEC.md §11.3; LOCAL-SPEC.md §9.7).</summary>
internal enum EvictionOutcome
{
    /// <summary>Not stored.</summary>
    NotStored = 0,

    /// <summary>Evicted: the file is unlinked and the index forgot it; it is never claimed again.</summary>
    Evicted = 1,

    /// <summary>Refused: a pin holds it.</summary>
    Pinned = 2,

    /// <summary>Refused: it was promised with <c>Have</c> and a <c>Want</c> for it is outstanding.</summary>
    Promised = 3,

    /// <summary>A reader holds the file without delete sharing: still stored, evicted on a later attempt.</summary>
    Later = 4,
}
