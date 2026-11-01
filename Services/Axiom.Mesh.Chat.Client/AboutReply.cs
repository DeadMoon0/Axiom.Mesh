using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>The reply to <c>about</c> (CHAT-SERVICE.md §5.1).</summary>
public sealed record AboutReply : IWireRecord<AboutReply>
{
    /// <summary>The namespace the service speaks: <see cref="ChatProtocol.Namespace"/>.</summary>
    public string Namespace { get; init; } = "";

    /// <summary>The service's version.</summary>
    public string Version { get; init; } = "";

    /// <summary>The store epoch the index was built under (LOCAL-SPEC.md §9.3).</summary>
    public Hash32 StoreEpoch { get; init; }

    /// <summary>The store sequence of the last Entry the index took in from the Host's stream; 0 when none.</summary>
    public ulong IndexedSeq { get; init; }

    /// <summary>Whether the Host holds no Entry of the chat kinds the index has not taken in: a timeline read now is complete.</summary>
    public bool CaughtUp { get; init; }

    /// <summary>The Channels indexed.</summary>
    public uint Channels { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out AboutReply? value)
    {
        var read = new AboutReply
        {
            Namespace = Wire.ReadString(ref reader, ChatLimits.NameLength),
            Version = Wire.ReadString(ref reader, ChatLimits.NameLength),
            StoreEpoch = Hash32.Read(ref reader),
            IndexedSeq = reader.UInt64BigEndian(),
            CaughtUp = reader.Boolean(),
            Channels = reader.UInt32BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Namespace, ChatLimits.NameLength);
        Wire.WriteString(ref writer, Version, ChatLimits.NameLength);
        StoreEpoch.Write(ref writer);
        writer.UInt64BigEndian(IndexedSeq);
        writer.Boolean(CaughtUp);
        writer.UInt32BigEndian(Channels);
    }
}
