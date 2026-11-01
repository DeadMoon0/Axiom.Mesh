using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>The reply to <c>attach</c> (CHAT-SERVICE.md §5.8): the job that does it.</summary>
public sealed record AttachReply : IWireRecord<AttachReply>
{
    /// <summary>The job's number; its <c>job</c> events carry it.</summary>
    public uint Job { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out AttachReply? value)
    {
        var read = new AttachReply { Job = reader.UInt32BigEndian() };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => writer.UInt32BigEndian(Job);
}
