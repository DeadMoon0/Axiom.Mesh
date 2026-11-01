using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat;

/// <summary>The <c>Error</c> record of a refused call (LOCAL-SPEC.md §5.14), its message and detail cut to their bounds between characters. Pure.</summary>
internal static class ErrorText
{
    private const int MessageBytes = 256;
    private const int DetailBytes = 1_024;

    /// <summary>The error.</summary>
    /// <param name="message">One sentence.</param>
    /// <param name="detail">The field at fault, or the platform's message.</param>
    /// <returns>The record.</returns>
    public static Error Of(string message, string detail) =>
        new() { Message = Timeline.Preview(Clean(message), MessageBytes), Detail = Timeline.Preview(Clean(detail), DetailBytes) };

    /// <summary>The refusal a verdict stands for.</summary>
    /// <param name="verdict">The verdict; not a pass.</param>
    /// <returns>The reply.</returns>
    public static CallReply Refused(Verdict verdict) => CallReply.Failure(verdict.Status, Of(verdict.Problem, verdict.Field));

    /// <summary>The refusal of a request the Host refused, passed on with its status and message.</summary>
    /// <param name="status">The Host's status.</param>
    /// <param name="error">The Host's error, if any.</param>
    /// <returns>The reply.</returns>
    public static CallReply Passed(Status status, Error? error) => CallReply.Failure(status, Of(error?.Message ?? string.Empty, error?.Detail ?? string.Empty));

    private static string Clean(string? text) => text?.Replace('\0', ' ') ?? string.Empty;
}
