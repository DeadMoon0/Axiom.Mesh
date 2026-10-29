using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>Dial</c> (374, LOCAL-SPEC.md §6.6): Starts a dial under SPEC.md §4.4; the outcome is an event on topic <c>peers</c>.</summary>
public sealed record Dial : LocalRequest, IRequest<Dial, NoContent>
{
    /// <summary>The message id, 374 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 374;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "Dial";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.6).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The device; all zero for whatever device answers at the address.</summary>
    public DeviceId DeviceId { get; init; }

    /// <summary>The address; empty for the addresses discovery knows for the device.</summary>
    public string Address { get; init; } = "";

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
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Dial? value)
    {
        var read = new Dial
        {
            DeviceId = DeviceId.Read(ref reader),
            Address = Wire.ReadString(ref reader, FieldBounds.AddressLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        DeviceId.Write(ref writer);
        Wire.WriteString(ref writer, Address, FieldBounds.AddressLength);
    }
}
