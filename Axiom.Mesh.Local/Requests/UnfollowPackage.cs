using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>UnfollowPackage</c> (309, LOCAL-SPEC.md §6.4): Stops following a Package and releases its Head and Policy pins; <c>Invalid</c> for an own Package.</summary>
public sealed record UnfollowPackage : LocalRequest, IRequest<UnfollowPackage, Package>
{
    /// <summary>The message id, 309 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 309;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "UnfollowPackage";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.4).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The Package.</summary>
    public required PackageId PackageId { get; init; }

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
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out UnfollowPackage? value)
    {
        var read = new UnfollowPackage
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
