using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ListServices</c> (384, LOCAL-SPEC.md §6.7): Lists every configured service, ascending by name.</summary>
public sealed record ListServices : LocalRequest, IRequest<ListServices, IReadOnlyList<Service>>
{
    private static readonly ListServices Empty = new();

    /// <summary>The message id, 384 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 384;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ListServices";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.7).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out IReadOnlyList<Service> ok)
    {
        ok = LocalWire.ReadList<Service>(ref reader, FieldBounds.ServicesCount);
        return reader.Ok;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, IReadOnlyList<Service> ok)
    {
        LocalWire.WriteList(ref writer, ok, FieldBounds.ServicesCount);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ListServices? value)
    {
        value = Empty;
        return true;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
    }
}
