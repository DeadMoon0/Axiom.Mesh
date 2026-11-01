using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>
/// <c>get</c> (CHAT-SERVICE.md §5.9): fetches one attachment of a message from the peers and writes it to a path, a file
/// for a ChunkList, a folder for a Tree, as a job whose <c>job</c> events report it.
/// </summary>
public sealed record GetRequest : IChatMethod<GetRequest, GetReply>
{
    /// <inheritdoc />
    public static string Method => ChatMethods.Get;

    /// <summary>The attachment's hash, as the message names it.</summary>
    public ObjectHash Hash { get; init; }

    /// <summary>The message that names it.</summary>
    public ObjectHash Message { get; init; }

    /// <summary>Where to write it: an absolute path the service can write.</summary>
    public string Path { get; init; } = "";

    /// <summary>What to do when the path exists: <c>Fail</c>, <c>Replace</c>, and for a folder also <c>Merge</c>.</summary>
    public ExistingPath Existing { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out GetRequest? value)
    {
        var read = new GetRequest
        {
            Hash = ObjectHash.Read(ref reader),
            Message = ObjectHash.Read(ref reader),
            Path = Wire.ReadString(ref reader, ChatLimits.PathLength),
            Existing = (ExistingPath)reader.UInt8(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Hash.Write(ref writer);
        Message.Write(ref writer);
        Wire.WriteString(ref writer, Path, ChatLimits.PathLength);
        writer.UInt8((byte)Existing);
    }
}
