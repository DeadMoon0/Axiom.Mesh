using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>GetObject</c> (316, LOCAL-SPEC.md §6.4): Returns the bytes of a stored object of any tag, Rosters included.</summary>
public sealed record GetObject : LocalRequest, IRequest<GetObject, ReadOnlyMemory<byte>>
{
    /// <summary>The message id, 316 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 316;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "GetObject";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.4).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The object.</summary>
    public required ObjectHash Hash { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out ReadOnlyMemory<byte> ok)
    {
        ok = LocalWire.ReadBlob(ref reader, FieldBounds.ObjectLength);
        return reader.Ok;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, ReadOnlyMemory<byte> ok)
    {
        LocalWire.WriteBlob(ref writer, ok, FieldBounds.ObjectLength);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out GetObject? value)
    {
        var read = new GetObject
        {
            Hash = ObjectHash.Read(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Hash.Write(ref writer);
    }
}
