using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>TransferChanged</c> (1027, LOCAL-SPEC.md §7.3): An object was wanted, received, sent, not found, cancelled or dropped.</summary>
public sealed record TransferChanged : LocalEvent, IEvent<TransferChanged>
{
    /// <summary>The message id, 1027 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 1027;

    /// <summary>The topic whose subscriptions receive it (LOCAL-SPEC.md §7.2).</summary>
    public static Topic Topic => Topic.Transfers;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The transfer.</summary>
    public required Transfer Transfer { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out TransferChanged? value)
    {
        var read = new TransferChanged
        {
            Transfer = LocalWire.ReadRecord<Transfer>(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Transfer.Write(ref writer);
    }
}
