using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>CancelPairing</c> (274, LOCAL-SPEC.md §6.2): Cancels the current pairing.</summary>
public sealed record CancelPairing : LocalRequest, IRequest<CancelPairing, Pairing>
{
    /// <summary>The message id, 274 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 274;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "CancelPairing";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.2).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The current pairing.</summary>
    public required uint PairingId { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out Pairing ok)
    {
        bool read = Pairing.TryRead(ref reader, out Pairing? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, Pairing ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out CancelPairing? value)
    {
        var read = new CancelPairing
        {
            PairingId = reader.UInt32BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt32BigEndian(PairingId);
    }
}
