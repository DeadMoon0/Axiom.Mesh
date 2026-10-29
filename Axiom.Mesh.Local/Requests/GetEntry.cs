using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>GetEntry</c> (315, LOCAL-SPEC.md §6.4): Returns a stored Entry; <c>NotFound</c> when held, evicted or never seen.</summary>
public sealed record GetEntry : LocalRequest, IRequest<GetEntry, Entry>
{
    /// <summary>The message id, 315 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 315;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "GetEntry";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.4).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The Entry object.</summary>
    public required ObjectHash Hash { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out Entry ok)
    {
        bool read = Entry.TryRead(ref reader, out Entry? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, Entry ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out GetEntry? value)
    {
        var read = new GetEntry
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
