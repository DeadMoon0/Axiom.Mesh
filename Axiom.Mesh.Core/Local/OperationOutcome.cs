using System;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>How an operation's work ended (LOCAL-SPEC.md §8.4): done with its kind's result body, or failed with a status.</summary>
internal sealed class OperationOutcome
{
    private OperationOutcome(Status status, ReadOnlyMemory<byte> result, string message)
    {
        Status = status;
        Result = result;
        Message = message;
    }

    /// <summary><see cref="Status.Ok"/> when done, else the reason it failed.</summary>
    public Status Status { get; }

    /// <summary>The kind's result body (§8.6) when done; empty otherwise.</summary>
    public ReadOnlyMemory<byte> Result { get; }

    /// <summary>The explanation, for display.</summary>
    public string Message { get; }

    /// <summary>The work is done.</summary>
    /// <param name="result">The kind's result body, at most 4096 bytes.</param>
    /// <returns>The outcome.</returns>
    public static OperationOutcome Done(ReadOnlyMemory<byte> result) => new(Status.Ok, result, string.Empty);

    /// <summary>The work failed.</summary>
    /// <param name="status">A status from the kind's row of §8.6.</param>
    /// <param name="message">The explanation.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentException"><paramref name="status"/> is <see cref="Status.Ok"/> or <see cref="Status.Cancelled"/>.</exception>
    public static OperationOutcome Failed(Status status, string message)
    {
        if (status is Status.Ok or Status.Cancelled)
            throw new ArgumentException("A failed operation carries a failure status; Cancelled is the table's own.", nameof(status));
        return new OperationOutcome(status, default, message ?? string.Empty);
    }
}
