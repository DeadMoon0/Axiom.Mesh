using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>
/// <c>attach</c> (CHAT-SERVICE.md §5.8): stores a file (as a ChunkList) or a folder (as a Tree) as the service's own user,
/// as a job whose <c>job</c> events report it; the done job's <see cref="JobView.Attachment"/> goes into <c>send</c>.
/// </summary>
public sealed record AttachRequest : IChatMethod<AttachRequest, AttachReply>
{
    /// <inheritdoc />
    public static string Method => ChatMethods.Attach;

    /// <summary>The file or folder, an absolute path the service can read.</summary>
    public string Path { get; init; } = "";

    /// <summary>The name shown, at most <see cref="ChatLimits.AttachmentNameLength"/> bytes; empty for the path's last name.</summary>
    public string Name { get; init; } = "";

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out AttachRequest? value)
    {
        var read = new AttachRequest
        {
            Path = Wire.ReadString(ref reader, ChatLimits.PathLength),
            Name = Wire.ReadString(ref reader, ChatLimits.AttachmentNameLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Path, ChatLimits.PathLength);
        Wire.WriteString(ref writer, Name, ChatLimits.AttachmentNameLength);
    }
}
