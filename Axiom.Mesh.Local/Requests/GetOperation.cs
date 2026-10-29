using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>GetOperation</c> (417, LOCAL-SPEC.md §8.5): Returns one of this connection's operations.</summary>
public sealed record GetOperation : LocalRequest, IRequest<GetOperation, Operation>
{
    /// <summary>The message id, 417 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 417;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "GetOperation";

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
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out GetOperation? value)
    {
        var read = new GetOperation
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
