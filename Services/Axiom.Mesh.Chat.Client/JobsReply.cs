using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>The reply to <c>jobs</c> (CHAT-SERVICE.md §5.10).</summary>
public sealed record JobsReply : IWireRecord<JobsReply>
{
    /// <summary>The jobs, by number, at most <see cref="ChatLimits.JobsListed"/>.</summary>
    public IReadOnlyList<JobView> Jobs { get; init; } = [];

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out JobsReply? value)
    {
        var read = new JobsReply { Jobs = ChatWire.ReadList<JobView>(ref reader, ChatLimits.JobsListed) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => ChatWire.WriteList(ref writer, Jobs, ChatLimits.JobsListed);
}
