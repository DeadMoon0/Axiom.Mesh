using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>Materialise</c> (317, LOCAL-SPEC.md §6.4): Starts writing a Tree or a ChunkList to the file system as an operation of kind Materialise; the OK body is the operation id.</summary>
public sealed record Materialise : LocalRequest, IRequest<Materialise, uint>
{
    /// <summary>The message id, 317 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 317;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "Materialise";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.4).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>A followed Package whose Entries reference the object.</summary>
    public required PackageId PackageId { get; init; }

    /// <summary>A Tree or a ChunkList.</summary>
    public required ObjectHash Hash { get; init; }

    /// <summary>The directory for a Tree or the file for a ChunkList; absolute.</summary>
    public required string Path { get; init; }

    /// <summary>What to do when the path exists.</summary>
    public required ExistingPath Existing { get; init; }

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
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Materialise? value)
    {
        var read = new Materialise
        {
            PackageId = PackageId.Read(ref reader),
            Hash = ObjectHash.Read(ref reader),
            Path = Wire.ReadString(ref reader, FieldBounds.PathLength),
            Existing = (ExistingPath)reader.UInt8(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        PackageId.Write(ref writer);
        Hash.Write(ref writer);
        Wire.WriteString(ref writer, Path, FieldBounds.PathLength);
        writer.UInt8((byte)Existing);
    }
}
