using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>UpdatePackage</c> (307, LOCAL-SPEC.md §6.4): Produces a Manifest with seq one higher carrying the given fields; the owner only, with the user key.</summary>
public sealed record UpdatePackage : LocalRequest, IRequest<UpdatePackage, Package>
{
    /// <summary>The message id, 307 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 307;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "UpdatePackage";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.4).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The Package.</summary>
    public required PackageId PackageId { get; init; }

    /// <summary>The new name; <see langword="null"/> leaves it unchanged.</summary>
    public string? Name { get; init; }

    /// <summary>The new retention; <see langword="null"/> leaves it unchanged.</summary>
    public Retention? Retention { get; init; }

    /// <summary>The new branches, ascending by name, unique; <see langword="null"/> leaves them unchanged.</summary>
    public IReadOnlyList<Branch>? Branches { get; init; }

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
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out UpdatePackage? value)
    {
        var read = new UpdatePackage
        {
            PackageId = PackageId.Read(ref reader),
            Name = reader.Boolean() ? Wire.ReadString(ref reader, FieldBounds.PackageNameLength) : null,
            Retention = reader.Boolean() ? (Retention)reader.UInt8() : null,
            Branches = reader.Boolean() ? LocalWire.ReadBranches(ref reader, FieldBounds.BranchesCount) : null,
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        PackageId.Write(ref writer);
        if (Name is { } presentName)
        {
            writer.Boolean(true);
            Wire.WriteString(ref writer, presentName, FieldBounds.PackageNameLength);
        }
        else
        {
            writer.Boolean(false);
        }
        if (Retention is { } presentRetention)
        {
            writer.Boolean(true);
            writer.UInt8((byte)presentRetention);
        }
        else
        {
            writer.Boolean(false);
        }
        if (Branches is { } presentBranches)
        {
            writer.Boolean(true);
            LocalWire.WriteBranches(ref writer, presentBranches, FieldBounds.BranchesCount);
        }
        else
        {
            writer.Boolean(false);
        }
    }
}
