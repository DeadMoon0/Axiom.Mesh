using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ListHistory</c> (314, LOCAL-SPEC.md §6.4): Walks a Package's stored Entries in the total order of SPEC.md §9.3, a page at a time.</summary>
public sealed record ListHistory : LocalRequest, IRequest<ListHistory, EntryPage>
{
    /// <summary>The message id, 314 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 314;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ListHistory";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.4).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The Package.</summary>
    public required PackageId PackageId { get; init; }

    /// <summary>The cursor: the last Entry of the previous page; <see langword="null"/> from the start.</summary>
    public EntryKey? After { get; init; }

    /// <summary>Descending instead of ascending.</summary>
    public bool Reverse { get; init; }

    /// <summary>Only these Entry kinds, ascending; empty for every kind.</summary>
    public IReadOnlyList<EntryKind> Kinds { get; init; } = [];

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
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ListHistory? value)
    {
        var read = new ListHistory
        {
            PackageId = PackageId.Read(ref reader),
            After = reader.Boolean() ? LocalWire.ReadEntryKey(ref reader) : null,
            Reverse = reader.Boolean(),
            Kinds = LocalWire.ReadEntryKinds(ref reader, FieldBounds.KindsCount),
            Limit = reader.UInt16BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        PackageId.Write(ref writer);
        if (After is { } presentAfter)
        {
            writer.Boolean(true);
            LocalWire.WriteEntryKey(ref writer, presentAfter);
        }
        else
        {
            writer.Boolean(false);
        }
        writer.Boolean(Reverse);
        LocalWire.WriteEntryKinds(ref writer, Kinds, FieldBounds.KindsCount);
        writer.UInt16BigEndian(Limit);
    }
}
