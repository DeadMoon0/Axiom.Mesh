namespace Axiom.Mesh.Chat;

/// <summary>
/// Applies a redaction to its target in either arrival order (SPEC.md §9.4, CHAT-SERVICE.md §11): honoured when its
/// envelope's <c>redacts</c> names the target, both are in one Channel, the target is no redaction itself, and the
/// redaction was written by the target's own device or a device of the Channel's owner (the Model's
/// <see cref="Redactions.Honours"/>, the rule the Host applies to <c>Entry.redacted</c>); waiting while the target is not
/// indexed. Pure.
/// </summary>
internal static class RedactionApplication
{
    /// <summary>Decides.</summary>
    /// <param name="redaction">The redaction's facts.</param>
    /// <param name="target">The target's facts; <see langword="null"/> when it is not indexed yet.</param>
    /// <param name="fromOwnerDevice">The redaction's device is a device of the Channel's owner.</param>
    /// <returns>Applied, Pending or Ignored.</returns>
    public static RedactionOutcome Decide(EntryFacts redaction, EntryFacts? target, bool fromOwnerDevice) =>
        target is not { } present ? RedactionOutcome.Pending
        : Redactions.Honours(redaction, present, fromOwnerDevice) ? RedactionOutcome.Applied
        : RedactionOutcome.Ignored;
}
