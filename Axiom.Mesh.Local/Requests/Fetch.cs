using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>Fetch</c> (318, LOCAL-SPEC.md §6.4): Starts fetching an object from peers as an operation of kind Fetch; the OK body is the operation id.</summary>
public sealed record Fetch : LocalRequest, IRequest<Fetch, uint>
{
    /// <summary>The message id, 318 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 318;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "Fetch";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.4).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>A followed Package whose Entries reference the object.</summary>
    public required PackageId PackageId { get; init; }

    /// <summary>The object.</summary>
    public required ObjectHash Hash { get; init; }

    /// <summary>Also the objects it references, through Trees and ChunkLists.</summary>
    public bool Recursive { get; init; }

    /// <summary>Pin what was fetched, with reason User or Service by role.</summary>
    public bool Pin { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out uint ok)
    {
        ok = reader.UInt32BigEndian();
        return reader.Ok;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, uint ok)
    {
        writer.UInt32BigEndian(ok);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Fetch? value)
    {
        var read = new Fetch
        {
            PackageId = PackageId.Read(ref reader),
            Hash = ObjectHash.Read(ref reader),
            Recursive = reader.Boolean(),
            Pin = reader.Boolean(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        PackageId.Write(ref writer);
        Hash.Write(ref writer);
        writer.Boolean(Recursive);
        writer.Boolean(Pin);
    }
}
