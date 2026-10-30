using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>Pin</c> (353, LOCAL-SPEC.md §6.5): The request <c>Pin</c> (353): pins a stored object with reason User (user role) or Service (service role); idempotent.</summary>
public sealed record PinObject : LocalRequest, IRequest<PinObject, Pin>
{
    /// <summary>The message id, 353 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 353;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "PinObject";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.5).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The object.</summary>
    public required ObjectHash Hash { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out Pin ok)
    {
        bool read = Pin.TryRead(ref reader, out Pin? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, Pin ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out PinObject? value)
    {
        var read = new PinObject
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
