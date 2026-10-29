using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>EntryStored</c> (1025, LOCAL-SPEC.md §7.3): An Entry was stored; in store-sequence order, each storing once.</summary>
public sealed record EntryStored : LocalEvent, IEvent<EntryStored>
{
    /// <summary>The message id, 1025 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 1025;

    /// <summary>The topic whose subscriptions receive it (LOCAL-SPEC.md §7.2).</summary>
    public static Topic Topic => Topic.Entries;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The stored Entry.</summary>
    public required Entry Entry { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out EntryStored? value)
    {
        var read = new EntryStored
        {
            Entry = LocalWire.ReadRecord<Entry>(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Entry.Write(ref writer);
    }
}
