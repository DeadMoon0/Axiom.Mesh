using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>AddStaticPeer</c> (372, LOCAL-SPEC.md §6.6): Adds a static peer address.</summary>
public sealed record AddStaticPeer : LocalRequest, IRequest<AddStaticPeer, StaticPeer>
{
    /// <summary>The message id, 372 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 372;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "AddStaticPeer";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.6).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary><c>host:port</c>.</summary>
    public required string Address { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out StaticPeer ok)
    {
        bool read = StaticPeer.TryRead(ref reader, out StaticPeer? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, StaticPeer ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out AddStaticPeer? value)
    {
        var read = new AddStaticPeer
        {
            Address = Wire.ReadString(ref reader, FieldBounds.AddressLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Address, FieldBounds.AddressLength);
    }
}
