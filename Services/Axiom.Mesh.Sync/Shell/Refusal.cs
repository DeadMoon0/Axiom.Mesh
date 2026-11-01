using Axiom.Mesh.Local;

namespace Axiom.Mesh.Sync;

/// <summary>The refusal of a call (LOCAL-SPEC.md §5.14, §11.2): a status, one sentence and the field at fault, cut to their bounds.</summary>
internal static class Refusal
{
    private const int MessageBytes = 256;
    private const int DetailBytes = 1_024;

    /// <summary>A refusal with a sync status.</summary>
    /// <param name="status">The status.</param>
    /// <param name="message">One sentence.</param>
    /// <param name="detail">The field at fault.</param>
    /// <returns>The reply.</returns>
    public static CallReply Of(SyncStatus status, string message, string detail) => Of((Status)(ushort)status, message, detail);

    /// <summary>A refusal with a status of LOCAL-SPEC.md §11.1.</summary>
    /// <param name="status">The status.</param>
    /// <param name="message">One sentence.</param>
    /// <param name="detail">The field at fault.</param>
    /// <returns>The reply.</returns>
    public static CallReply Of(Status status, string message, string detail) =>
        CallReply.Failure(status, new Error { Message = TextCut.Of(message, MessageBytes), Detail = TextCut.Of(detail, DetailBytes) });
}
