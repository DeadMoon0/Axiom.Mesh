using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ListGrants</c> (311, LOCAL-SPEC.md §6.4): Lists a Package's current Grants, ascending by grantee.</summary>
public sealed record ListGrants : LocalRequest, IRequest<ListGrants, IReadOnlyList<Grant>>
{
    /// <summary>The message id, 311 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 311;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ListGrants";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.4).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The Package.</summary>
    public required PackageId PackageId { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out IReadOnlyList<Grant> ok)
    {
        ok = LocalWire.ReadList<Grant>(ref reader, FieldBounds.GrantsCount);
        return reader.Ok;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, IReadOnlyList<Grant> ok)
    {
        LocalWire.WriteList(ref writer, ok, FieldBounds.GrantsCount);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ListGrants? value)
    {
        var read = new ListGrants
        {
            PackageId = PackageId.Read(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        PackageId.Write(ref writer);
    }
}
