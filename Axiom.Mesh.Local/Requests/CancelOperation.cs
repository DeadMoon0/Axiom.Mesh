using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>CancelOperation</c> (418, LOCAL-SPEC.md §8.5): Cancels an operation; on an ended one it is OK and changes nothing.</summary>
public sealed record CancelOperation : LocalRequest, IRequest<CancelOperation, Operation>
{
    /// <summary>The message id, 418 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 418;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "CancelOperation";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §8.5).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The operation.</summary>
    public required uint OperationId { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out Operation ok)
    {
        bool read = Operation.TryRead(ref reader, out Operation? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, Operation ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out CancelOperation? value)
    {
        var read = new CancelOperation
        {
            OperationId = reader.UInt32BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt32BigEndian(OperationId);
    }
}
