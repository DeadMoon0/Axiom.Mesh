using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary><c>jobs</c> (CHAT-SERVICE.md §5.10): every running job and the ended ones the service still remembers. Empty.</summary>
public sealed record JobsRequest : IChatMethod<JobsRequest, JobsReply>
{
    /// <inheritdoc />
    public static string Method => ChatMethods.Jobs;

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out JobsRequest? value)
    {
        value = reader.Ok ? new JobsRequest() : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
    }
}
