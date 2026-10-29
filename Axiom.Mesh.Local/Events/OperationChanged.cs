using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>OperationChanged</c> (1028, LOCAL-SPEC.md §7.3): An operation of this connection started, progressed or ended.</summary>
public sealed record OperationChanged : LocalEvent, IEvent<OperationChanged>
{
    /// <summary>The message id, 1028 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 1028;

    /// <summary>The topic whose subscriptions receive it (LOCAL-SPEC.md §7.2).</summary>
    public static Topic Topic => Topic.Operations;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The operation.</summary>
    public required Operation Operation { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out OperationChanged? value)
    {
        var read = new OperationChanged
        {
            Operation = LocalWire.ReadRecord<Operation>(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Operation.Write(ref writer);
    }
}
