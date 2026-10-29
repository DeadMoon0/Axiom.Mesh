using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>GetStorePath</c> (516, LOCAL-SPEC.md §9.7): Returns the store directory for direct, read-only reads.</summary>
public sealed record GetStorePath : LocalRequest, IRequest<GetStorePath, StorePath>
{
    private static readonly GetStorePath Empty = new();

    /// <summary>The message id, 516 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 516;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "GetStorePath";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §9.7).</summary>
    public static Roles AllowedRoles => Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out StorePath ok)
    {
        bool read = StorePath.TryRead(ref reader, out StorePath? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, StorePath ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out GetStorePath? value)
    {
        value = Empty;
        return true;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
    }
}
