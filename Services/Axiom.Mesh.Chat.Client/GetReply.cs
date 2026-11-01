using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>The reply to <c>get</c> (CHAT-SERVICE.md §5.9): the job that does it.</summary>
public sealed record GetReply : IWireRecord<GetReply>
{
    /// <summary>The job's number; its <c>job</c> events carry it.</summary>
    public uint Job { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out GetReply? value)
    {
        var read = new GetReply { Job = reader.UInt32BigEndian() };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => writer.UInt32BigEndian(Job);
}
