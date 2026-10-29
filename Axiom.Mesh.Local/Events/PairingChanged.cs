using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>PairingChanged</c> (1029, LOCAL-SPEC.md §7.3): The current pairing changed state or a field.</summary>
public sealed record PairingChanged : LocalEvent, IEvent<PairingChanged>
{
    /// <summary>The message id, 1029 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 1029;

    /// <summary>The topic whose subscriptions receive it (LOCAL-SPEC.md §7.2).</summary>
    public static Topic Topic => Topic.Pairing;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The pairing.</summary>
    public required Pairing Pairing { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out PairingChanged? value)
    {
        var read = new PairingChanged
        {
            Pairing = LocalWire.ReadRecord<Pairing>(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Pairing.Write(ref writer);
    }
}
