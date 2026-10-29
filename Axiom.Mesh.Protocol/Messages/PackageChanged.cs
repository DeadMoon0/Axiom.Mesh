using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>PackageChanged</c> (3, SPEC.md §7.3), Established only: the state the sender would list for one Package changed.
/// Never sent while a listing is incomplete (<see cref="ListingRule"/>).
/// </summary>
public sealed class PackageChanged : MeshMessage, IWireMessage<PackageChanged>
{
    /// <summary>A change of one Package's state.</summary>
    /// <param name="state">The new state.</param>
    public PackageChanged(PackageState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        State = state;
    }

    /// <inheritdoc />
    public override ushort Id => MessageIds.PackageChanged;

    /// <summary>The Package's new state.</summary>
    public PackageState State { get; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out PackageChanged message)
    {
        message = new PackageChanged(PackageState.Read(ref reader));
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer) => State.Write(ref writer);

    /// <inheritdoc />
    public override string ToString() => $"PackageChanged {State}";
}
