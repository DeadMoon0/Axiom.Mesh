using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>FollowPackage</c> (308, LOCAL-SPEC.md §6.4): Follows a Listed Package; on a Followed one sets the policy and changes nothing else.</summary>
public sealed record FollowPackage : LocalRequest, IRequest<FollowPackage, Package>
{
    /// <summary>The message id, 308 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 308;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "FollowPackage";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.4).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The Package.</summary>
    public required PackageId PackageId { get; init; }

    /// <summary>The local retention policy (§14.14).</summary>
    public LocalPolicy Policy { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out Package ok)
    {
        bool read = Package.TryRead(ref reader, out Package? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, Package ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out FollowPackage? value)
    {
        var read = new FollowPackage
        {
            PackageId = PackageId.Read(ref reader),
            Policy = (LocalPolicy)reader.UInt8(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        PackageId.Write(ref writer);
        writer.UInt8((byte)Policy);
    }
}
