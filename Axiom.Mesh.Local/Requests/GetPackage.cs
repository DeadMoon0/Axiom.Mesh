using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>GetPackage</c> (305, LOCAL-SPEC.md §6.4): Returns one Package.</summary>
public sealed record GetPackage : LocalRequest, IRequest<GetPackage, Package>
{
    /// <summary>The message id, 305 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 305;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "GetPackage";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.4).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

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
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out GetPackage? value)
    {
        var read = new GetPackage
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
