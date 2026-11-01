using System;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat;

/// <summary>
/// Who wrote a message, as this Host knows it (CHAT-SERVICE.md §4 <c>AuthorView</c>): the device's user from the Rosters
/// held (LOCAL-SPEC.md §6.2, §6.3), with both names; a device in no Roster held here is shown by its id alone. Also the
/// devices of a Channel's owner, for SPEC.md §9.4. Pure.
/// </summary>
internal static class AuthorResolution
{
    /// <summary>The author of a device.</summary>
    /// <param name="device">The writing device.</param>
    /// <param name="users">The users known here.</param>
    /// <param name="devices">The devices of their Rosters, removed ones included.</param>
    /// <returns>The author.</returns>
    public static AuthorView Of(DeviceId device, IReadOnlyList<User> users, IReadOnlyList<Device> devices)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(devices);
        Device? known = devices.FirstOrDefault(d => d.DeviceId == device);
        if (known is null)
            return new AuthorView { DeviceId = device };
        User? user = users.FirstOrDefault(u => u.UserId == known.UserId);
        return new AuthorView { DeviceId = device, UserId = known.UserId, UserName = user?.Name ?? string.Empty, DeviceName = known.Name };
    }

    /// <summary>Whether a device belongs to a user's Roster (removed devices count: their Entries stay valid, SPEC.md §9.6).</summary>
    /// <param name="device">The device.</param>
    /// <param name="user">The user, a Channel's owner.</param>
    /// <param name="devices">The devices of the Rosters held here.</param>
    /// <returns><see langword="true"/> when it is one of the user's devices.</returns>
    public static bool IsDeviceOf(DeviceId device, UserId user, IReadOnlyList<Device> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        return devices.Any(d => d.DeviceId == device && d.UserId == user);
    }
}
