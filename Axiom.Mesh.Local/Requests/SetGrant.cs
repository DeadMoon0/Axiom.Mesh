using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>SetGrant</c> (312, LOCAL-SPEC.md §6.4): Produces a Grant with seq one higher for a grantee; the owner only, with the user key; a user-key request.</summary>
public sealed record SetGrant : LocalRequest, IRequest<SetGrant, Grant>
{
    /// <summary>The message id, 312 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 312;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "SetGrant";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.4).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The Package.</summary>
    public required PackageId PackageId { get; init; }

    /// <summary>A known user other than the owner.</summary>
    public required UserId GranteeId { get; init; }

    /// <summary>Read, Write, or None to revoke.</summary>
    public required GrantRights Rights { get; init; }

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
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out SetGrant? value)
    {
        var read = new SetGrant
        {
            PackageId = PackageId.Read(ref reader),
            GranteeId = UserId.Read(ref reader),
            Rights = (GrantRights)reader.UInt8(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        PackageId.Write(ref writer);
        GranteeId.Write(ref writer);
        writer.UInt8((byte)Rights);
    }
}
