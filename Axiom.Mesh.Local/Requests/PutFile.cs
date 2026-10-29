using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>PutFile</c> (515, LOCAL-SPEC.md §9.5): Starts chunking and storing a file as an operation of kind PutFile; the OK body is the operation id.</summary>
public sealed record PutFile : LocalRequest, IRequest<PutFile, uint>
{
    /// <summary>The message id, 515 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 515;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "PutFile";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §9.5).</summary>
    public static Roles AllowedRoles => Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>A regular file the Host can read; absolute.</summary>
    public required string Path { get; init; }

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
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out PutFile? value)
    {
        var read = new PutFile
        {
            Path = Wire.ReadString(ref reader, FieldBounds.PathLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Path, FieldBounds.PathLength);
    }
}
