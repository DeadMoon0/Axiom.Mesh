using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ListPackages</c> (304, LOCAL-SPEC.md §6.4): Lists the Packages this Host knows, a page at a time, ascending by id.</summary>
public sealed record ListPackages : LocalRequest, IRequest<ListPackages, PackagePage>
{
    /// <summary>The message id, 304 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 304;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ListPackages";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.4).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The cursor: the last Package id of the previous page; <see langword="null"/> from the start.</summary>
    public PackageId? After { get; init; }

    /// <summary>Only these kinds, ascending; empty for every kind.</summary>
    public IReadOnlyList<PackageKind> Kinds { get; init; } = [];

    /// <summary>Only Packages of this owner; <see langword="null"/> for any.</summary>
    public UserId? OwnerId { get; init; }

    /// <summary>Only followed Packages.</summary>
    public bool FollowedOnly { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out PackagePage ok)
    {
        bool read = PackagePage.TryRead(ref reader, out PackagePage? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, PackagePage ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ListPackages? value)
    {
        var read = new ListPackages
        {
            After = reader.Boolean() ? Axiom.Mesh.PackageId.Read(ref reader) : null,
            Kinds = LocalWire.ReadPackageKinds(ref reader, FieldBounds.KindsCount),
            OwnerId = reader.Boolean() ? Axiom.Mesh.UserId.Read(ref reader) : null,
            FollowedOnly = reader.Boolean(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        if (After is { } presentAfter)
        {
            writer.Boolean(true);
            presentAfter.Write(ref writer);
        }
        else
        {
            writer.Boolean(false);
        }
        LocalWire.WritePackageKinds(ref writer, Kinds, FieldBounds.KindsCount);
        if (OwnerId is { } presentOwnerId)
        {
            writer.Boolean(true);
            presentOwnerId.Write(ref writer);
        }
        else
        {
            writer.Boolean(false);
        }
        writer.Boolean(FollowedOnly);
    }
}
