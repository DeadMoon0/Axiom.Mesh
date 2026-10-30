using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// How a write of an Entry on this device ended (LOCAL-SPEC.md §9.4): stored with its hash and store sequence, or refused
/// with the status of §11.1 and one line that says why.
/// </summary>
/// <param name="Status"><c>Ok</c>, or why not: <c>NoIdentity</c>, <c>NotFound</c>, <c>NotWritable</c>, <c>ClockExhausted</c>, <c>Invalid</c>, <c>StoreFull</c>.</param>
/// <param name="Hash">The Entry's hash; zero when refused.</param>
/// <param name="StoreSeq">Its store sequence; 0 when refused.</param>
/// <param name="Message">Why it was refused; empty when stored.</param>
internal readonly record struct EntryWritten(Status Status, ObjectHash Hash, ulong StoreSeq, string Message)
{
    /// <summary>A refusal.</summary>
    /// <param name="status">Why.</param>
    /// <param name="message">One line for the user.</param>
    /// <returns>The outcome.</returns>
    public static EntryWritten Refused(Status status, string message) => new(status, default, 0, message);
}
