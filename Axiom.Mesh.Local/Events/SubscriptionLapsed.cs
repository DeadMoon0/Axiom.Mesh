using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>SubscriptionLapsed</c> (1034, LOCAL-SPEC.md §7.3): The subscription ended because its queue overflowed (§7.4); re-subscribe, for topic entries with <c>FromSeq</c> one past <see cref="LastSeq"/>.</summary>
public sealed record SubscriptionLapsed : LocalEvent, IEvent<SubscriptionLapsed>
{
    /// <summary>The message id, 1034 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 1034;

    /// <summary>The topic whose subscriptions receive it (LOCAL-SPEC.md §7.2).</summary>
    public static Topic Topic => Topic.Unassigned;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>Events discarded.</summary>
    public uint Dropped { get; init; }

    /// <summary>Topic entries: the store sequence of the last <c>EntryStored</c> handed to the pipe; else 0.</summary>
    public ulong LastSeq { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out SubscriptionLapsed? value)
    {
        var read = new SubscriptionLapsed
        {
            Dropped = reader.UInt32BigEndian(),
            LastSeq = reader.UInt64BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt32BigEndian(Dropped);
        writer.UInt64BigEndian(LastSeq);
    }
}
