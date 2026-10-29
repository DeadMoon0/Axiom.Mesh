namespace Axiom.Mesh;

/// <summary>Whether a stored Redaction withdraws a stored Entry (SPEC.md §9.4; LOCAL-SPEC.md §5.6 <c>redacted</c>).</summary>
public static class Redactions
{
    /// <summary>
    /// Whether <paramref name="redaction"/> is honoured for <paramref name="target"/>, whatever order they were stored in:
    /// both in the same Package, the target not itself a Redaction, and the Redaction written by the target's own device
    /// or by a device of the owner (SPEC.md §9.4). An honoured target keeps its bytes, but its payload is never shown.
    /// </summary>
    /// <param name="redaction">The Redaction Entry whose payload names <paramref name="target"/>.</param>
    /// <param name="target">The Entry it names.</param>
    /// <param name="redactionFromAnOwnerDevice">Whether the Redaction's device is a device of the Package's owner.</param>
    /// <returns><see langword="true"/> when the Redaction is honoured; <see langword="false"/> when it is ignored.</returns>
    public static bool Honours(EntryFacts redaction, EntryFacts target, bool redactionFromAnOwnerDevice) =>
        redaction.Kind == EntryKind.Redaction
        && target.Kind != EntryKind.Redaction
        && redaction.Package == target.Package
        && (redaction.Key.Device == target.Key.Device || redactionFromAnOwnerDevice);
}
