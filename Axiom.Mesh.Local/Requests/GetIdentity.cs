using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>GetIdentity</c> (256, LOCAL-SPEC.md §6.1): Returns this device's identity.</summary>
public sealed record GetIdentity : LocalRequest, IRequest<GetIdentity, Identity>
{
    private static readonly GetIdentity Empty = new();

    /// <summary>The message id, 256 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 256;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "GetIdentity";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.1).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out Identity ok)
    {
        bool read = Identity.TryRead(ref reader, out Identity? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, Identity ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out GetIdentity? value)
    {
        value = Empty;
        return true;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
    }
}
