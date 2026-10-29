using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>PeerChanged</c> (1026, LOCAL-SPEC.md §7.3): A peer was discovered, dialled, established a session, lost one, refused or was refused, or was forgotten.</summary>
public sealed record PeerChanged : LocalEvent, IEvent<PeerChanged>
{
    /// <summary>The message id, 1026 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 1026;

    /// <summary>The topic whose subscriptions receive it (LOCAL-SPEC.md §7.2).</summary>
    public static Topic Topic => Topic.Peers;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>What happened (§14.11).</summary>
    public PeerChange Change { get; init; }

    /// <summary>The peer after the change.</summary>
    public required Peer Peer { get; init; }

    /// <summary>The session, for Established and Closed.</summary>
    public Session? Session { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out PeerChanged? value)
    {
        var read = new PeerChanged
        {
            Change = (PeerChange)reader.UInt8(),
            Peer = LocalWire.ReadRecord<Peer>(ref reader),
            Session = reader.Boolean() ? LocalWire.ReadRecord<Session>(ref reader) : null,
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt8((byte)Change);
        Peer.Write(ref writer);
        if (Session is { } presentSession)
        {
            writer.Boolean(true);
            presentSession.Write(ref writer);
        }
        else
        {
            writer.Boolean(false);
        }
    }
}
