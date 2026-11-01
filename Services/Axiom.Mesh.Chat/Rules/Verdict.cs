using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat;

/// <summary>
/// What a rule of the chat service decided about a call: go ahead, or refuse with a status and the sentence the caller
/// gets as its <c>Error.message</c>. Only the two factories make one.
/// </summary>
internal sealed record Verdict
{
    private Verdict(Status status, string problem, string field)
    {
        Status = status;
        Problem = problem;
        Field = field;
    }

    /// <summary>Go ahead.</summary>
    public static Verdict Pass { get; } = new(Status.Ok, string.Empty, string.Empty);

    /// <summary><c>Ok</c>, or the status a refusal answers.</summary>
    public Status Status { get; }

    /// <summary>Why not, in one sentence; empty for <see cref="Pass"/>.</summary>
    public string Problem { get; }

    /// <summary>The request's field at fault, for <c>Error.detail</c>; empty when none.</summary>
    public string Field { get; }

    /// <summary>Whether the call goes ahead.</summary>
    public bool IsPass => Status == Status.Ok;

    /// <summary>A refusal with a status of LOCAL-SPEC.md §11.1.</summary>
    /// <param name="status">The status.</param>
    /// <param name="problem">Why, in one sentence.</param>
    /// <param name="field">The field at fault, or empty.</param>
    /// <returns>The verdict.</returns>
    public static Verdict Refuse(Status status, string problem, string field) => new(status, problem, field);

    /// <summary>A refusal with a status of the chat service's own.</summary>
    /// <param name="status">The status.</param>
    /// <param name="problem">Why, in one sentence.</param>
    /// <param name="field">The field at fault, or empty.</param>
    /// <returns>The verdict.</returns>
    public static Verdict Refuse(ChatStatus status, string problem, string field) => new((Status)(ushort)status, problem, field);
}
