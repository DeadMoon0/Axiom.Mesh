using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// A callback on the wire (LOCAL-SPEC.md §3.6): the Host-chosen callback id and the callback. The service answers it
/// with exactly one <see cref="Reply"/> of the same id before the callback's deadline.
/// </summary>
public sealed class Callback : LocalMessage, IHostMessage
{
    /// <summary>Puts a callback in a frame.</summary>
    /// <param name="callbackId">The callback id, never 0, unique among the outstanding callbacks of the connection.</param>
    /// <param name="call">The callback.</param>
    /// <exception cref="ArgumentException"><paramref name="callbackId"/> is 0.</exception>
    public Callback(uint callbackId, LocalCallback call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (callbackId == 0)
            throw new ArgumentException("A callback id is never 0 (LOCAL-SPEC.md §3.2).", nameof(callbackId));
        CallbackId = callbackId;
        Call = call;
    }

    /// <summary>The callback id the <see cref="Reply"/> carries back.</summary>
    public uint CallbackId { get; }

    /// <summary>The callback.</summary>
    public LocalCallback Call { get; }

    /// <inheritdoc />
    public override ushort MessageId => Call.MessageId;

    /// <inheritdoc />
    public override string ToString() => $"!{CallbackId} {Call}";

    internal override void WritePayload(ref WireWriter writer)
    {
        writer.UInt32BigEndian(CallbackId);
        Call.Write(ref writer);
    }
}
