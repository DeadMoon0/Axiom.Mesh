using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>What <c>status</c> shows: the Host, this device's identity, the store and the peers. Pure.</summary>
internal static class StatusView
{
    /// <summary>The status as names and values.</summary>
    /// <param name="host">Where the Host was reached.</param>
    /// <param name="ack">The Host's answer to the <c>Hello</c>.</param>
    /// <param name="identity">The identity now.</param>
    /// <param name="store">The store now.</param>
    /// <param name="peers">The peers now.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Text(string host, HelloAck ack, Identity identity, Store store, IReadOnlyList<Peer> peers) => TextTable.Pairs(
    [
        ("Host", $"{host}, version {(ack.HostVersion.Length == 0 ? "unknown" : ack.HostVersion)}"),
        ("Identity", identity.Present
            ? $"user {Named(identity.UserName)} ({Formats.Short(identity.UserId.Value)}), device {Named(identity.DeviceName)} ({Formats.Short(identity.DeviceId.Value)})"
            : "none; run 'axiom-mesh identity create --user <name> --device <name>', or enrol this device with 'axiom-mesh pair show --device'"),
        ("Roster", identity.Present ? $"seq {identity.RosterSeq.ToString(CultureInfo.InvariantCulture)}, user key {(identity.UserKeyPresent ? "on this device" : "on another device")}" : "-"),
        ("Store", $"{store.Objects.ToString(CultureInfo.InvariantCulture)} objects, {Formats.Bytes(store.Bytes)}, budget {(store.Budget == 0 ? "none" : Formats.Bytes(store.Budget))}"),
        ("Peers", $"{peers.Count.ToString(CultureInfo.InvariantCulture)} known, {peers.Count(p => p.State == PeerState.Established).ToString(CultureInfo.InvariantCulture)} established"),
    ]);

    /// <summary>The status as JSON.</summary>
    /// <param name="host">Where the Host was reached.</param>
    /// <param name="ack">The Host's answer to the <c>Hello</c>.</param>
    /// <param name="identity">The identity now.</param>
    /// <param name="store">The store now.</param>
    /// <param name="peers">The peers now.</param>
    /// <returns>The document.</returns>
    public static string Json(string host, HelloAck ack, Identity identity, Store store, IReadOnlyList<Peer> peers) => JsonOut.Of(w =>
    {
        w.WriteStartObject();
        w.WriteString("host", host);
        w.WriteString("hostVersion", ack.HostVersion);
        w.WriteStartObject("identity");
        w.WriteBoolean("present", identity.Present);
        JsonOut.Id(w, "userId", identity.UserId.Value);
        w.WriteString("userName", identity.UserName);
        w.WriteBoolean("userKeyPresent", identity.UserKeyPresent);
        JsonOut.Id(w, "deviceId", identity.DeviceId.Value);
        w.WriteString("deviceName", identity.DeviceName);
        w.WriteNumber("rosterSeq", identity.RosterSeq);
        w.WriteNumber("created", identity.Created);
        w.WriteEndObject();
        w.WriteStartObject("store");
        w.WriteNumber("objects", store.Objects);
        w.WriteNumber("bytes", store.Bytes);
        w.WriteNumber("budget", store.Budget);
        w.WriteEndObject();
        w.WriteStartObject("peers");
        w.WriteNumber("known", peers.Count);
        w.WriteNumber("established", peers.Count(p => p.State == PeerState.Established));
        w.WriteEndObject();
        w.WriteEndObject();
    });

    private static string Named(string name) => name.Length == 0 ? "(unnamed)" : name;
}
