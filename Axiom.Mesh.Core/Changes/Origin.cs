namespace Axiom.Mesh.Core;

/// <summary>
/// Who an object or a change came from: this node, or a peer's device. The held buffer is bounded per origin (SPEC.md
/// §10.6) and a change is pushed to every peer but its origin (§7.3, §10.5).
/// </summary>
/// <param name="Kind">Local or a peer.</param>
/// <param name="Peer">The peer's device; zero for <see cref="OriginKind.Local"/>.</param>
internal readonly record struct Origin(OriginKind Kind, DeviceId Peer)
{
    /// <summary>This node.</summary>
    public static Origin Local => default;

    /// <summary>A peer.</summary>
    /// <param name="device">The peer's device.</param>
    /// <returns>The origin.</returns>
    public static Origin FromPeer(DeviceId device) => new(OriginKind.Peer, device);

    /// <inheritdoc />
    public override string ToString() => Kind == OriginKind.Local ? "local" : "peer " + Peer;
}
