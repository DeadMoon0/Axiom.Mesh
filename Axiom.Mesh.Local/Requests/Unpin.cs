using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>Unpin</c> (354, LOCAL-SPEC.md §6.5): Removes this role's pin on each object; never fails for a pin that is not there. The OK body counts the removed pins.</summary>
public sealed record Unpin : LocalRequest, IRequest<Unpin, ushort>
{
    /// <summary>The message id, 354 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 354;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "Unpin";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.5).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The objects, ascending as byte strings.</summary>
    public IReadOnlyList<ObjectHash> Hashes { get; init; } = [];

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out ushort ok)
    {
        ok = reader.UInt16BigEndian();
        return reader.Ok;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, ushort ok)
    {
        writer.UInt16BigEndian(ok);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Unpin? value)
    {
        var read = new Unpin
        {
            Hashes = LocalWire.ReadHashes(ref reader, FieldBounds.UnpinCount),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        LocalWire.WriteHashes(ref writer, Hashes, FieldBounds.UnpinCount);
    }
}
