namespace Axiom.Mesh.Core;

/// <summary>What an operation's work reports while it runs (LOCAL-SPEC.md §8.3); events are coalesced to one per second.</summary>
internal sealed class OperationProgress
{
    private readonly OperationTable table;
    private readonly LocalOperation operation;

    /// <summary>The progress of one operation.</summary>
    /// <param name="table">Its table.</param>
    /// <param name="operation">The operation.</param>
    public OperationProgress(OperationTable table, LocalOperation operation)
    {
        this.table = table;
        this.operation = operation;
    }

    /// <summary>Reports how far the work is.</summary>
    /// <param name="done">Units done.</param>
    /// <param name="total">Units in all; 0 when unknown.</param>
    /// <param name="message">For display.</param>
    public void Report(ulong done, ulong total, string message) => table.Progress(operation, done, total, message);
}
