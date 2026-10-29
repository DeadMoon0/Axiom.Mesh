using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ListStored</c> (319, LOCAL-SPEC.md §6.4): Walks stored Entries in store-sequence order, for an indexer that catches up.</summary>
public sealed record ListStored : LocalRequest, IRequest<ListStored, EntryPage>
{
    /// <summary>The message id, 319 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 319;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ListStored";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.4).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The Package; all zero for every Package.</summary>
    public PackageId PackageId { get; init; }

    /// <summary>Entry kinds; empty for every kind (user) or the registered kinds (service).</summary>
    public IReadOnlyList<EntryKind> Kinds { get; init; } = [];

    /// <summary>The first store sequence to return; 0 reads as 1.</summary>
    public ulong FromSeq { get; init; }

    /// <summary>At most this many Entries, 1 to 64.</summary>
    public required ushort Limit { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out EntryPage ok)
    {
        bool read = EntryPage.TryRead(ref reader, out EntryPage? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, EntryPage ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ListStored? value)
    {
        var read = new ListStored
        {
            PackageId = PackageId.Read(ref reader),
            Kinds = LocalWire.ReadEntryKinds(ref reader, FieldBounds.KindsCount),
            FromSeq = reader.UInt64BigEndian(),
            Limit = reader.UInt16BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        PackageId.Write(ref writer);
        LocalWire.WriteEntryKinds(ref writer, Kinds, FieldBounds.KindsCount);
        writer.UInt64BigEndian(FromSeq);
        writer.UInt16BigEndian(Limit);
    }
}
