using System.Diagnostics.CodeAnalysis;
using System.Net;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// LOCAL-SPEC.md §6.6 <c>Dial</c>, as a function of trust: both fields empty is <c>Invalid</c>, an address must be an
/// <c>IP:port</c> (<c>Invalid</c>; a host name is for <c>AddStaticPeer</c>), a device must be known and not removed
/// (<c>NotFound</c>) and not established already (<c>AlreadyExists</c>).
/// </summary>
internal static class DialRule
{
    /// <summary>Decides a <c>Dial</c>.</summary>
    /// <param name="trust">The node's trust.</param>
    /// <param name="device">The device; zero for whatever answers at the address.</param>
    /// <param name="text">The address; empty for the ones discovery knows.</param>
    /// <param name="established">Whether the device has an established session.</param>
    /// <param name="address">The address parsed, or <see langword="null"/> when none was given.</param>
    /// <returns><see langword="null"/> when the dial may start; else why not.</returns>
    public static RequestRefusal? Check(TrustSnapshot trust, DeviceId device, string text, bool established, out IPEndPoint? address)
    {
        address = null;
        bool noDevice = device.Value.IsZero;
        if (noDevice && text.Length == 0)
            return new RequestRefusal(Status.Invalid, "Name a device, an address, or both.", "deviceId");
        if (text.Length != 0 && !TryParse(text, out address))
            return new RequestRefusal(Status.Invalid, $"\"{text}\" is not IP:port.", "address");
        if (noDevice)
            return null;
        if (!trust.TryGetDevice(device, out TrustedDevice known) || known.IsRemoved)
            return new RequestRefusal(Status.NotFound, $"Device {device} is not a device of a known user here.", "deviceId");
        return established ? new RequestRefusal(Status.AlreadyExists, $"Device {device} has an established session already.", "deviceId") : null;
    }

    private static bool TryParse(string text, [NotNullWhen(true)] out IPEndPoint? address) =>
        IPEndPoint.TryParse(text, out address) && address.Port != 0 && text.Contains(':', System.StringComparison.Ordinal);
}
