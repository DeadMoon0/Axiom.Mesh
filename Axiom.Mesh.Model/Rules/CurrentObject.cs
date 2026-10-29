namespace Axiom.Mesh;

/// <summary>
/// Which of two valid Manifests (per Package), Rosters (per user) or Grants (per Package and grantee) is current: the
/// one with the higher <c>seq</c>, ties broken by the lower hash (SPEC.md §8.7, §8.8, §8.9). A node keeps the newest it
/// has seen, so a peer can serve an older state but never roll one back (§13).
/// </summary>
public static class CurrentObject
{
    /// <summary>Whether <paramref name="candidate"/> replaces <paramref name="current"/>.</summary>
    /// <param name="candidate">The newly verified object's stamp.</param>
    /// <param name="current">The stamp of the object held as current.</param>
    /// <returns><see langword="true"/> for a higher <c>seq</c>, or an equal one with a lower hash; never for the same object.</returns>
    public static bool Supersedes(ObjectSeq candidate, ObjectSeq current) =>
        candidate.Seq > current.Seq || (candidate.Seq == current.Seq && candidate.Hash < current.Hash);

    /// <summary>
    /// Whether a Manifest changes its Package's kind, which no Manifest may: such a Manifest is faulty content and
    /// ignored (SPEC.md §8.7).
    /// </summary>
    /// <param name="candidate">The newly verified Manifest.</param>
    /// <param name="current">The current Manifest of the same Package.</param>
    /// <returns><see langword="true"/> when the kinds differ.</returns>
    public static bool ChangesKind(ManifestContent candidate, ManifestContent current)
    {
        System.ArgumentNullException.ThrowIfNull(candidate);
        System.ArgumentNullException.ThrowIfNull(current);
        return candidate.Kind != current.Kind;
    }
}
