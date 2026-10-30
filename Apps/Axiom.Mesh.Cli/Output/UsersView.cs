using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>The known users (LOCAL-SPEC.md §5.2) and the devices of one (§5.3), as text and as JSON. Pure.</summary>
internal static class UsersView
{
    /// <summary>The users as a table.</summary>
    /// <param name="users">The users.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Text(IReadOnlyList<User> users) => TextTable.Lines(
        ["USER", "NAME", "SELF", "ROSTER", "DEVICES", "KNOWN SINCE"],
        users.Select(u => (IReadOnlyList<string>)[Formats.Short(u.UserId.Value), Name(u.Name), u.Self ? "self" : string.Empty, Number(u.RosterSeq), Number(u.Devices), Formats.Time(u.KnownSince)]),
        "no users are known");

    /// <summary>The users as JSON.</summary>
    /// <param name="users">The users.</param>
    /// <returns>The document.</returns>
    public static string Json(IReadOnlyList<User> users) => JsonOut.Of(w =>
    {
        w.WriteStartArray();
        foreach (User u in users)
        {
            w.WriteStartObject();
            JsonOut.Id(w, "userId", u.UserId.Value);
            w.WriteString("name", u.Name);
            w.WriteBoolean("self", u.Self);
            w.WriteNumber("knownSince", u.KnownSince);
            w.WriteNumber("rosterSeq", u.RosterSeq);
            w.WriteNumber("devices", u.Devices);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    });

    /// <summary>The devices as a table.</summary>
    /// <param name="devices">The devices.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> DevicesText(IReadOnlyList<Device> devices) => TextTable.Lines(
        ["DEVICE", "NAME", "USER", "SELF", "REMOVED", "ADDED", "LAST SEEN"],
        devices.Select(d => (IReadOnlyList<string>)[Formats.Short(d.DeviceId.Value), Name(d.Name), Formats.Short(d.UserId.Value), d.Self ? "self" : string.Empty, d.Removed ? "removed" : string.Empty, Formats.Time(d.AddedAt), Formats.Time(d.LastSeen)]),
        "no devices");

    /// <summary>The devices as JSON.</summary>
    /// <param name="devices">The devices.</param>
    /// <returns>The document.</returns>
    public static string DevicesJson(IReadOnlyList<Device> devices) => JsonOut.Of(w =>
    {
        w.WriteStartArray();
        foreach (Device d in devices)
        {
            w.WriteStartObject();
            JsonOut.Id(w, "deviceId", d.DeviceId.Value);
            JsonOut.Id(w, "userId", d.UserId.Value);
            w.WriteString("name", d.Name);
            w.WriteNumber("addedAt", d.AddedAt);
            w.WriteBoolean("removed", d.Removed);
            w.WriteBoolean("self", d.Self);
            w.WriteNumber("lastSeen", d.LastSeen);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    });

    private static string Name(string name) => name.Length == 0 ? "-" : name;

    private static string Number(ulong value) => value.ToString(CultureInfo.InvariantCulture);
}
