using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>RevokeGrant</c> (313, LOCAL-SPEC.md §6.4): <c>SetGrant</c> with rights None; <c>NotFound</c> for a grantee without a current Grant; a user-key request.</summary>
public sealed record RevokeGrant : LocalRequest, IRequest<RevokeGrant, Grant>
{
    /// <summary>The message id, 313 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 313;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "RevokeGrant";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.4).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The Package.</summary>
    public required PackageId PackageId { get; init; }

    /// <summary>The grantee.</summary>
    public required UserId GranteeId { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out Grant ok)
    {
        bool read = Grant.TryRead(ref reader, out Grant? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, Grant ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out RevokeGrant? value)
    {
        var read = new RevokeGrant
        {
            PackageId = PackageId.Read(ref reader),
            GranteeId = UserId.Read(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        PackageId.Write(ref writer);
        GranteeId.Write(ref writer);
    }
}
