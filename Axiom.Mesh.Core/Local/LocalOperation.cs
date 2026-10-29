using System;
using System.Threading;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// One operation of one connection (LOCAL-SPEC.md §8): its id, kind and unit, its own cancellation linked to the
/// connection's, and the mutable state its <see cref="OperationTable"/> changes under its lock. An ended operation never
/// changes again.
/// </summary>
internal sealed class LocalOperation : IDisposable
{
    /// <summary>A running operation.</summary>
    /// <param name="id">Its id, unique for the connection's lifetime.</param>
    /// <param name="kind">Its kind (§14.6).</param>
    /// <param name="unit">What <c>done</c> and <c>total</c> count.</param>
    /// <param name="startedMs">When it started.</param>
    /// <param name="connection">The connection's token; ending the connection cancels the operation.</param>
    public LocalOperation(uint id, OperationKind kind, ProgressUnit unit, long startedMs, CancellationToken connection)
    {
        Id = id;
        Kind = kind;
        Unit = unit;
        StartedMs = startedMs;
        UpdatedMs = startedMs;
        Cancellation = CancellationTokenSource.CreateLinkedTokenSource(connection);
    }

    /// <summary>The operation id.</summary>
    public uint Id { get; }

    /// <summary>Its kind.</summary>
    public OperationKind Kind { get; }

    /// <summary>What it counts.</summary>
    public ProgressUnit Unit { get; }

    /// <summary>When it started.</summary>
    public long StartedMs { get; }

    /// <summary>Cancelled by <c>CancelOperation</c>, <c>Drain</c> or the connection's end.</summary>
    public CancellationTokenSource Cancellation { get; }

    /// <summary>Where it stands.</summary>
    public OperationState State { get; set; } = OperationState.Running;

    /// <summary>The last change.</summary>
    public long UpdatedMs { get; set; }

    /// <summary>Units done.</summary>
    public ulong Done { get; set; }

    /// <summary>Units in all; 0 when unknown.</summary>
    public ulong Total { get; set; }

    /// <summary>For display.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>The reason when Failed, <c>Cancelled</c> when Cancelled, else Ok.</summary>
    public Status Status { get; set; }

    /// <summary>The kind's result body once Done.</summary>
    public ReadOnlyMemory<byte> Result { get; set; }

    /// <summary>Whether the <c>Response</c> that started it was written, so its events may follow (§7.2).</summary>
    public bool Announced { get; set; }

    /// <summary>When its last progress event was emitted; events are at most once per second.</summary>
    public long LastEmittedMs { get; set; } = long.MinValue;

    /// <summary>Whether it ended.</summary>
    public bool HasEnded => State != OperationState.Running;

    /// <summary>The record of §5.13 as it stands.</summary>
    /// <returns>The record.</returns>
    public Operation Record() => new()
    {
        OperationId = Id,
        Kind = Kind,
        State = State,
        Started = LocalRecords.Milliseconds(StartedMs),
        Updated = LocalRecords.Milliseconds(UpdatedMs),
        Done = Done,
        Total = Total,
        Unit = Unit,
        Message = Message,
        Status = Status,
        Result = Result,
    };

    /// <inheritdoc />
    public void Dispose() => Cancellation.Dispose();
}
