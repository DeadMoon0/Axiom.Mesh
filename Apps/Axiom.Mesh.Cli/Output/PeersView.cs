using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>The peers (LOCAL-SPEC.md §5.7) and the sessions (§5.8), as text and as JSON. Pure.</summary>
internal static class PeersView
{
    /// <summary>The peers as a table.</summary>
    /// <param name="peers">The peers.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Text(IReadOnlyList<Peer> peers) => TextTable.Lines(
        ["DEVICE", "USER", "STATE", "KNOWN", "ADDRESSES", "LAST SEEN", "VIOLATIONS"],
        peers.Select(p => (IReadOnlyList<string>)[
            Formats.Short(p.DeviceId.Value),
            Formats.Short(p.UserId.Value),
            Formats.Word(p.State),
            Formats.YesNo(p.Known),
            p.Addresses.Count == 0 ? "-" : string.Join(",", p.Addresses),
            Formats.Time(p.LastSeen),
            p.Violations.ToString(CultureInfo.InvariantCulture)]),
        "no peers");

    /// <summary>The peers as JSON.</summary>
    /// <param name="peers">The peers.</param>
    /// <returns>The document.</returns>
    public static string Json(IReadOnlyList<Peer> peers) => JsonOut.Of(w =>
    {
        w.WriteStartArray();
        foreach (Peer p in peers)
        {
            w.WriteStartObject();
            JsonOut.Id(w, "deviceId", p.DeviceId.Value);
            JsonOut.Id(w, "userId", p.UserId.Value);
            w.WriteBoolean("known", p.Known);
            w.WriteStartArray("addresses");
            foreach (string address in p.Addresses)
                w.WriteStringValue(address);
            w.WriteEndArray();
            w.WriteNumber("sources", (byte)p.Sources);
            w.WriteNumber("firstSeen", p.FirstSeen);
            w.WriteNumber("lastSeen", p.LastSeen);
            w.WriteString("state", Formats.Word(p.State));
            w.WriteNumber("backoffUntil", p.BackoffUntil);
            w.WriteNumber("violations", p.Violations);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    });

    /// <summary>The sessions as a table.</summary>
    /// <param name="sessions">The sessions.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> SessionsText(IReadOnlyList<Session> sessions) => TextTable.Lines(
        ["DEVICE", "USER", "DIRECTION", "REMOTE", "SINCE", "IN", "OUT", "OBJECTS IN", "OBJECTS OUT"],
        sessions.Select(s => (IReadOnlyList<string>)[
            Formats.Short(s.DeviceId.Value),
            Formats.Short(s.UserId.Value),
            Formats.Word(s.Direction),
            s.Remote.Length == 0 ? "-" : s.Remote,
            Formats.Time(s.Since),
            Formats.Bytes(s.BytesIn),
            Formats.Bytes(s.BytesOut),
            s.ObjectsIn.ToString(CultureInfo.InvariantCulture),
            s.ObjectsOut.ToString(CultureInfo.InvariantCulture)]),
        "no sessions");

    /// <summary>The sessions as JSON.</summary>
    /// <param name="sessions">The sessions.</param>
    /// <returns>The document.</returns>
    public static string SessionsJson(IReadOnlyList<Session> sessions) => JsonOut.Of(w =>
    {
        w.WriteStartArray();
        foreach (Session s in sessions)
        {
            w.WriteStartObject();
            JsonOut.Id(w, "deviceId", s.DeviceId.Value);
            JsonOut.Id(w, "userId", s.UserId.Value);
            w.WriteString("direction", Formats.Word(s.Direction));
            w.WriteString("remote", s.Remote);
            w.WriteString("local", s.Local);
            w.WriteNumber("since", s.Since);
            w.WriteNumber("lastReceived", s.LastReceived);
            w.WriteString("tlsSuite", s.TlsSuite);
            w.WriteString("state", Formats.Word(s.State));
            w.WriteNumber("bytesIn", s.BytesIn);
            w.WriteNumber("bytesOut", s.BytesOut);
            w.WriteNumber("objectsIn", s.ObjectsIn);
            w.WriteNumber("objectsOut", s.ObjectsOut);
            w.WriteNumber("wantsOut", s.WantsOut);
            w.WriteNumber("wantsIn", s.WantsIn);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    });
}
