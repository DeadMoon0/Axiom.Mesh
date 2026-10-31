using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>Long-running work the Host does for this connection (LOCAL-SPEC.md §5.13, §8).</summary>
public sealed record Operation : IWireRecord<Operation>
{
    /// <summary>The Host-chosen operation id, never 0.</summary>
    public uint OperationId { get; init; }

    /// <summary>What it does (§8.6).</summary>
    public OperationKind Kind { get; init; }

    /// <summary>Running, Done, Failed or Cancelled.</summary>
    public OperationState State { get; init; }

    /// <summary>When it started.</summary>
    public ulong Started { get; init; }

    /// <summary>The last change of any field below.</summary>
    public ulong Updated { get; init; }

    /// <summary>Progress, in <see cref="Unit"/>s.</summary>
    public ulong Done { get; init; }

    /// <summary>The total, in <see cref="Unit"/>s; 0 when unknown.</summary>
    public ulong Total { get; init; }

    /// <summary>What progress counts.</summary>
    public ProgressUnit Unit { get; init; }

    /// <summary>For display; MUST NOT be interpreted.</summary>
    public string Message { get; init; } = "";

    /// <summary>The reason when Failed, <c>Cancelled</c> when Cancelled; else <c>Ok</c>.</summary>
    public Status Status { get; init; }

    /// <summary>The kind's result body (§8.6) once Done; else empty.</summary>
    public ReadOnlyMemory<byte> Result { get; init; }

    /// <summary>
    /// Reads <see cref="Result"/> as the result body of the operation's kind (§8.6): <see cref="FetchResult"/> for <c>Fetch</c>.
    /// </summary>
    /// <typeparam name="TResult">The result body of the kind.</typeparam>
    /// <param name="result">The result body.</param>
    /// <returns><see langword="true"/> when the operation is Done and its result reads exactly as <typeparamref name="TResult"/>.</returns>
    public bool TryReadResult<TResult>([NotNullWhen(true)] out TResult? result)
        where TResult : class, IWireRecord<TResult>
    {
        result = null;
        if (State != OperationState.Done)
            return false;

        var reader = new WireReader(Result);
        return TResult.TryRead(ref reader, out result) && reader.IsAtEnd;
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Operation? value)
    {
        var read = new Operation
        {
            OperationId = reader.UInt32BigEndian(),
            Kind = (OperationKind)reader.UInt16BigEndian(),
            State = (OperationState)reader.UInt8(),
            Started = reader.UInt64BigEndian(),
            Updated = reader.UInt64BigEndian(),
            Done = reader.UInt64BigEndian(),
            Total = reader.UInt64BigEndian(),
            Unit = (ProgressUnit)reader.UInt8(),
            Message = Wire.ReadString(ref reader, FieldBounds.OperationMessageLength),
            Status = (Status)reader.UInt16BigEndian(),
            Result = LocalWire.ReadBlob(ref reader, FieldBounds.OperationResultLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        writer.UInt32BigEndian(OperationId);
        writer.UInt16BigEndian((ushort)Kind);
        writer.UInt8((byte)State);
        writer.UInt64BigEndian(Started);
        writer.UInt64BigEndian(Updated);
        writer.UInt64BigEndian(Done);
        writer.UInt64BigEndian(Total);
        writer.UInt8((byte)Unit);
        Wire.WriteString(ref writer, Message, FieldBounds.OperationMessageLength);
        writer.UInt16BigEndian((ushort)Status);
        LocalWire.WriteBlob(ref writer, Result, FieldBounds.OperationResultLength);
    }
}
