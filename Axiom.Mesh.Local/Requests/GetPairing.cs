using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>GetPairing</c> (275, LOCAL-SPEC.md §6.2): Returns the current or the last pairing; <c>NotFound</c> when there was none.</summary>
public sealed record GetPairing : LocalRequest, IRequest<GetPairing, Pairing>
{
    private static readonly GetPairing Empty = new();

    /// <summary>The message id, 275 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 275;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "GetPairing";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.2).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

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
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out GetPairing? value)
    {
        value = Empty;
        return true;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
    }
}
