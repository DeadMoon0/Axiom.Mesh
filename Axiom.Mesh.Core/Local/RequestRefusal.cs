using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// Why a request is refused, as a rule decides it: the status of LOCAL-SPEC.md §11.1, one line for the user, and the
/// field that is wrong (<c>Error.detail</c> for <c>Invalid</c>, §6). A rule returns one or <see langword="null"/>.
/// </summary>
/// <param name="Status">The status; never <c>Ok</c>.</param>
/// <param name="Message">One line that says what is wrong and, where it helps, what to do.</param>
/// <param name="Field">The field that is wrong; empty when none is.</param>
internal sealed record RequestRefusal(Status Status, string Message, string Field)
{
    /// <summary>The refusal as the request's answer.</summary>
    /// <param name="correlation">The request's correlation.</param>
    /// <returns>The answer.</returns>
    public LocalAnswer Answer(uint correlation) => LocalAnswer.Failure(correlation, Status, Message, Field);
}
