using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>RegisterKind</c> (512, LOCAL-SPEC.md §9.2): Registers a Package kind and its Entry kinds for this service while the connection lasts.</summary>
public sealed record RegisterKind : LocalRequest, IRequest<RegisterKind, KindRegistration>
{
    /// <summary>The message id, 512 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 512;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "RegisterKind";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §9.2).</summary>
    public static Roles AllowedRoles => Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>A Package kind its configuration lists.</summary>
    public required PackageKind Kind { get; init; }

    /// <summary>The Entry kinds of this Package kind, ascending, non-empty.</summary>
    public IReadOnlyList<EntryKind> EntryKinds { get; init; } = [];

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out KindRegistration ok)
    {
        bool read = KindRegistration.TryRead(ref reader, out KindRegistration? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, KindRegistration ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out RegisterKind? value)
    {
        var read = new RegisterKind
        {
            Kind = (PackageKind)reader.UInt16BigEndian(),
            EntryKinds = LocalWire.ReadEntryKinds(ref reader, FieldBounds.KindsCount),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt16BigEndian((ushort)Kind);
        LocalWire.WriteEntryKinds(ref writer, EntryKinds, FieldBounds.KindsCount);
    }
}
