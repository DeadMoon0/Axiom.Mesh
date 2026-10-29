using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>RemoveStaticPeer</c> (373, LOCAL-SPEC.md §6.6): Removes a static peer address.</summary>
public sealed record RemoveStaticPeer : LocalRequest, IRequest<RemoveStaticPeer, NoContent>
{
    /// <summary>The message id, 373 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 373;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "RemoveStaticPeer";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.6).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The address as configured.</summary>
    public required string Address { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out NoContent ok)
    {
        ok = default;
        return true;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, NoContent ok)
    {
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out RemoveStaticPeer? value)
    {
        var read = new RemoveStaticPeer
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
