namespace Axiom.Mesh;

/// <summary>Whether a stored redaction withdraws a stored Entry (SPEC.md §9.4; LOCAL-SPEC.md §5.6 <c>redacted</c>).</summary>
public static class Redactions
{
    /// <summary>
    /// Whether <paramref name="redaction"/> is honoured for <paramref name="target"/>, whatever order they were stored in:
    /// the redaction's envelope names the target in <c>redacts</c> (whatever its kind), both are in the same Package, the
    /// target is not itself a redaction, and the redaction was written by the target's own device or by a device of the
    /// owner (SPEC.md §9.4). An honoured target keeps its bytes, but its payload is never shown.
    /// </summary>
    /// <param name="redaction">The Entry whose <c>redacts</c> may name <paramref name="target"/>.</param>
    /// <param name="target">The Entry it may withdraw.</param>
    /// <param name="redactionFromAnOwnerDevice">Whether the redaction's device is a device of the Package's owner.</param>
    /// <returns><see langword="true"/> when the redaction is honoured; <see langword="false"/> when it is ignored.</returns>
    public static bool Honours(EntryFacts redaction, EntryFacts target, bool redactionFromAnOwnerDevice) =>
        redaction.IsRedaction
        && redaction.Redacts == target.Key.Hash
        && !target.IsRedaction
        && redaction.Package == target.Package
        && (redaction.Key.Device == target.Key.Device || redactionFromAnOwnerDevice);
}
