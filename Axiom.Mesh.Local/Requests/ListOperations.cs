using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ListOperations</c> (416, LOCAL-SPEC.md §8.5): Lists this connection's operations, ascending by id.</summary>
public sealed record ListOperations : LocalRequest, IRequest<ListOperations, IReadOnlyList<Operation>>
{
    private static readonly ListOperations Empty = new();

    /// <summary>The message id, 416 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 416;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ListOperations";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §8.5).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out IReadOnlyList<Operation> ok)
    {
        ok = LocalWire.ReadList<Operation>(ref reader, FieldBounds.OperationsCount);
        return reader.Ok;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, IReadOnlyList<Operation> ok)
    {
        LocalWire.WriteList(ref writer, ok, FieldBounds.OperationsCount);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ListOperations? value)
    {
        value = Empty;
        return true;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
    }
}
