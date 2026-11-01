using System.Collections.Generic;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat;

/// <summary>What a redaction's honouring needs to know of its Channel (SPEC.md §9.4): the owner and the devices of the Rosters held.</summary>
/// <param name="Owner">The Channel's owner.</param>
/// <param name="Devices">The devices of the Rosters held here, the owner's among them.</param>
internal sealed record ChannelOwner(UserId Owner, IReadOnlyList<Device> Devices)
{
    /// <summary>Whether a device is one of the owner's.</summary>
    /// <param name="device">The device.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool HasDevice(DeviceId device) => AuthorResolution.IsDeviceOf(device, Owner, Devices);
}
