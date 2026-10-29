using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>StoreChanged</c> (1033, LOCAL-SPEC.md §7.3): The store's counts or budget changed; at most once per second.</summary>
public sealed record StoreChanged : LocalEvent, IEvent<StoreChanged>
{
    /// <summary>The message id, 1033 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 1033;

    /// <summary>The topic whose subscriptions receive it (LOCAL-SPEC.md §7.2).</summary>
    public static Topic Topic => Topic.Store;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The store.</summary>
    public required Store Store { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out StoreChanged? value)
    {
        var read = new StoreChanged
        {
            Store = LocalWire.ReadRecord<Store>(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Store.Write(ref writer);
    }
}
