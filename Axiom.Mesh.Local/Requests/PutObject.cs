using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>PutObject</c> (514, LOCAL-SPEC.md §9.5): Stores an unsigned object and pins it with reason Service.</summary>
public sealed record PutObject : LocalRequest, IRequest<PutObject, StoredObject>
{
    /// <summary>The message id, 514 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 514;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "PutObject";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §9.5).</summary>
    public static Roles AllowedRoles => Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The bytes of one SPEC.md §8 object; signed objects are <c>Invalid</c>.</summary>
    public ReadOnlyMemory<byte> Object { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out StoredObject ok)
    {
        bool read = StoredObject.TryRead(ref reader, out StoredObject? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, StoredObject ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out PutObject? value)
    {
        var read = new PutObject
        {
            Object = LocalWire.ReadBlob(ref reader, FieldBounds.ObjectLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        LocalWire.WriteBlob(ref writer, Object, FieldBounds.ObjectLength);
    }
}
