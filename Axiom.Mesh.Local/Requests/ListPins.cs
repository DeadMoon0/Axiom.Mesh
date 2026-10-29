using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ListPins</c> (352, LOCAL-SPEC.md §6.5): Lists pins, a page at a time, ascending by key.</summary>
public sealed record ListPins : LocalRequest, IRequest<ListPins, PinPage>
{
    /// <summary>The message id, 352 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 352;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ListPins";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.5).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>Only pins of this object; <see langword="null"/> for any.</summary>
    public ObjectHash? Hash { get; init; }

    /// <summary>Only pins whose source is this Package; <see langword="null"/> for any.</summary>
    public PackageId? PackageId { get; init; }

    /// <summary>The cursor; <see langword="null"/> from the start.</summary>
    public PinKey? After { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out PinPage ok)
    {
        bool read = PinPage.TryRead(ref reader, out PinPage? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, PinPage ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ListPins? value)
    {
        var read = new ListPins
        {
            Hash = reader.Boolean() ? Axiom.Mesh.ObjectHash.Read(ref reader) : null,
            PackageId = reader.Boolean() ? Axiom.Mesh.PackageId.Read(ref reader) : null,
            After = reader.Boolean() ? LocalWire.ReadRecord<PinKey>(ref reader) : null,
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        if (Hash is { } presentHash)
        {
            writer.Boolean(true);
            presentHash.Write(ref writer);
        }
        else
        {
            writer.Boolean(false);
        }
        if (PackageId is { } presentPackageId)
        {
            writer.Boolean(true);
            presentPackageId.Write(ref writer);
        }
        else
        {
            writer.Boolean(false);
        }
        if (After is { } presentAfter)
        {
            writer.Boolean(true);
            presentAfter.Write(ref writer);
        }
        else
        {
            writer.Boolean(false);
        }
    }
}
