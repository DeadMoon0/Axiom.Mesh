using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>CreatePackage</c> (306, LOCAL-SPEC.md §6.4): Creates a Package owned by the own user with a Manifest of seq 1, followed; needs the user key.</summary>
public sealed record CreatePackage : LocalRequest, IRequest<CreatePackage, Package>
{
    /// <summary>The message id, 306 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 306;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "CreatePackage";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.4).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>Its kind; any kind is allowed.</summary>
    public required PackageKind Kind { get; init; }

    /// <summary>Its name; MUST NOT be empty.</summary>
    public required string Name { get; init; }

    /// <summary>The Manifest's retention.</summary>
    public Retention Retention { get; init; }

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
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out CreatePackage? value)
    {
        var read = new CreatePackage
        {
            Kind = (PackageKind)reader.UInt16BigEndian(),
            Name = Wire.ReadString(ref reader, FieldBounds.PackageNameLength),
            Retention = (Retention)reader.UInt8(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt16BigEndian((ushort)Kind);
        Wire.WriteString(ref writer, Name, FieldBounds.PackageNameLength);
        writer.UInt8((byte)Retention);
    }
}
