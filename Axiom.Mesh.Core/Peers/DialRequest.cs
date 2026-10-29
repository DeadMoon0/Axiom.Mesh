using System.Net;

namespace Axiom.Mesh.Core;

/// <summary>
/// One dial the peer table decided on: the address and, unless it is a static peer's, the device it should reach. While
/// it runs it is the attempt the session of its connection reports to, so that a close before the peer's <c>Hello</c>
/// counts as a refusal (SPEC.md §6.2).
/// </summary>
internal sealed class DialRequest
{
    /// <summary>A dial.</summary>
    /// <param name="address">Where to dial.</param>
    /// <param name="device">The device announced there; <see langword="null"/> for a static peer.</param>
    public DialRequest(IPEndPoint address, DeviceId? device)
    {
        Address = address;
        Device = device;
    }

    /// <summary>Where to dial.</summary>
    public IPEndPoint Address { get; }

    /// <summary>The device announced there; <see langword="null"/> for a static peer, which names none.</summary>
    public DeviceId? Device { get; }

    /// <summary>The device the handshake proved, once a session opened; written under the peer table's lock.</summary>
    public DeviceId? Reached { get; set; }

    /// <summary>Whether the session reached the peer's valid <c>Hello</c>; written under the peer table's lock.</summary>
    public bool Established { get; set; }

    /// <inheritdoc />
    public override string ToString() => Device is { } device ? $"{Address} ({device})" : $"{Address} (static)";
}
