using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>SetStoreBudget</c> (356, LOCAL-SPEC.md §6.5): Sets the store budget; eviction may start at once.</summary>
public sealed record SetStoreBudget : LocalRequest, IRequest<SetStoreBudget, Store>
{
    /// <summary>The message id, 356 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 356;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "SetStoreBudget";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.5).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The budget; 0 for the device default.</summary>
    public ulong Bytes { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out Store ok)
    {
        bool read = Store.TryRead(ref reader, out Store? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, Store ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out SetStoreBudget? value)
    {
        var read = new SetStoreBudget
        {
            Bytes = reader.UInt64BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt64BigEndian(Bytes);
    }
}
