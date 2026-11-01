using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>A job of the chat service (CHAT-SERVICE.md §8): an <c>attach</c> or a <c>get</c>, where it stands, and its result.</summary>
public sealed record JobView : IWireRecord<JobView>
{
    /// <summary>The job's number, unique while the service runs.</summary>
    public uint Job { get; init; }

    /// <summary>What it does.</summary>
    public JobKind Kind { get; init; }

    /// <summary>Where it stands.</summary>
    public JobState State { get; init; }

    /// <summary>Bytes done so far.</summary>
    public ulong Done { get; init; }

    /// <summary>Bytes in all, as far as known; 0 while unknown.</summary>
    public ulong Total { get; init; }

    /// <summary>For a <c>get</c>, the message whose attachment it fetches; zero for an <c>attach</c>.</summary>
    public ObjectHash Message { get; init; }

    /// <summary>
    /// How it ended: 0 while running and when done, else the status of LOCAL-SPEC.md §11.1 or <see cref="ChatStatus"/>
    /// that failed it.
    /// </summary>
    public ushort Status { get; init; }

    /// <summary>The attachment: for a done <c>attach</c> the one stored, ready for <c>send</c>; for a <c>get</c> the one fetched.</summary>
    public Attachment? Attachment { get; init; }

    /// <summary>The path read (<c>attach</c>) or written (<c>get</c>).</summary>
    public string Path { get; init; } = "";

    /// <summary>Why it failed, in one sentence; empty unless failed.</summary>
    public string Problem { get; init; } = "";

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out JobView? value)
    {
        var read = new JobView
        {
            Job = reader.UInt32BigEndian(),
            Kind = (JobKind)reader.UInt8(),
            State = (JobState)reader.UInt8(),
            Done = reader.UInt64BigEndian(),
            Total = reader.UInt64BigEndian(),
            Message = ObjectHash.Read(ref reader),
            Status = reader.UInt16BigEndian(),
            Attachment = ChatWire.ReadOptionalAttachment(ref reader),
            Path = Wire.ReadString(ref reader, ChatLimits.PathLength),
            Problem = Wire.ReadString(ref reader, ChatLimits.ProblemLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        writer.UInt32BigEndian(Job);
        writer.UInt8((byte)Kind);
        writer.UInt8((byte)State);
        writer.UInt64BigEndian(Done);
        writer.UInt64BigEndian(Total);
        Message.Write(ref writer);
        writer.UInt16BigEndian(Status);
        ChatWire.WriteOptionalAttachment(ref writer, Attachment);
        Wire.WriteString(ref writer, Path, ChatLimits.PathLength);
        Wire.WriteString(ref writer, Problem, ChatLimits.ProblemLength);
    }
}
