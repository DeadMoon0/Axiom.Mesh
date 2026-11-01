using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary><c>withdrawn</c> (CHAT-SERVICE.md §6.2): an honoured Redaction withdrew a message; an app stops showing its text.</summary>
public sealed record WithdrawnEvent : ChatEvent, IWireRecord<WithdrawnEvent>
{
    /// <inheritdoc />
    public override string Name => ChatEvents.Withdrawn;

    /// <summary>The withdrawn message's position in the total order.</summary>
    public EntryKey Key { get; init; }

    /// <summary>Its Channel.</summary>
    public PackageId PackageId { get; init; }

    /// <summary>The device whose Redaction withdrew it.</summary>
    public DeviceId By { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out WithdrawnEvent? value)
    {
        var read = new WithdrawnEvent
        {
            Key = ChatWire.ReadKey(ref reader),
            PackageId = PackageId.Read(ref reader),
            By = DeviceId.Read(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        ChatWire.WriteKey(ref writer, Key);
        PackageId.Write(ref writer);
        By.Write(ref writer);
    }
}
