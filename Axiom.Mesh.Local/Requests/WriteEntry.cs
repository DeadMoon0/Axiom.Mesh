using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>WriteEntry</c> (513, LOCAL-SPEC.md §9.4): Writes an Entry the Host builds, signs with the device key, stores and replicates; not idempotent.</summary>
public sealed record WriteEntry : LocalRequest, IRequest<WriteEntry, Entry>
{
    /// <summary>The message id, 513 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 513;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "WriteEntry";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §9.4).</summary>
    public static Roles AllowedRoles => Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>A followed Package of a kind this service registered.</summary>
    public required PackageId PackageId { get; init; }

    /// <summary>An Entry kind this service registered for that Package kind.</summary>
    public required EntryKind Kind { get; init; }

    /// <summary>Ascending as byte strings; empty for the Host to choose.</summary>
    public IReadOnlyList<ObjectHash> Parents { get; init; } = [];

    /// <summary>The payload, opaque to the Host, which never parses it.</summary>
    public ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>The objects the Entry needs, strictly ascending, at most 64; each must be stored already. They become the Entry's <c>references</c>.</summary>
    public IReadOnlyList<ObjectHash> References { get; init; } = [];

    /// <summary>The Entry this one withdraws (SPEC.md §9.4), or the zero hash for none. It becomes the Entry's <c>redacts</c>.</summary>
    public ObjectHash Redacts { get; init; }

    /// <summary>A request that writes <paramref name="body"/> to <paramref name="package"/> with the parents the Host chooses.</summary>
    /// <param name="package">A followed Package of a kind this service registered.</param>
    /// <param name="body">What the Entry says.</param>
    /// <returns>The request.</returns>
    public static WriteEntry Of(PackageId package, EntryBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new WriteEntry { PackageId = package, Kind = body.Kind, Payload = body.Payload, References = [.. body.References], Redacts = body.Redacts };
    }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out Entry ok)
    {
        bool read = Entry.TryRead(ref reader, out Entry? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, Entry ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out WriteEntry? value)
    {
        var read = new WriteEntry
        {
            PackageId = PackageId.Read(ref reader),
            Kind = (EntryKind)reader.UInt16BigEndian(),
            Parents = LocalWire.ReadHashes(ref reader, FieldBounds.ParentsCount),
            Payload = LocalWire.ReadBlob(ref reader, FieldBounds.PayloadLength),
            References = LocalWire.ReadHashes(ref reader, FieldBounds.ReferencesCount),
            Redacts = ObjectHash.Read(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        PackageId.Write(ref writer);
        writer.UInt16BigEndian((ushort)Kind);
        LocalWire.WriteHashes(ref writer, Parents, FieldBounds.ParentsCount);
        LocalWire.WriteBlob(ref writer, Payload, FieldBounds.PayloadLength);
        LocalWire.WriteHashes(ref writer, References, FieldBounds.ReferencesCount);
        Redacts.Write(ref writer);
    }
}
