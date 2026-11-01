using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>
/// <c>job</c> (CHAT-SERVICE.md §6.4): a job started, progressed (at most once a second) or ended (always). Emitted with
/// a zero Package, so every app subscribed to the service sees it.
/// </summary>
public sealed record JobEvent : ChatEvent, IWireRecord<JobEvent>
{
    /// <inheritdoc />
    public override string Name => ChatEvents.Job;

    /// <summary>The job as it stands.</summary>
    public JobView Job { get; init; } = new();

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out JobEvent? value)
    {
        var read = new JobEvent { Job = ChatWire.ReadRecord<JobView>(ref reader) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => Job.Write(ref writer);
}
