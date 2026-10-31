# Axiom.Mesh.Discovery

Finding [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh) nodes on a local network (SPEC.md §3): DNS-SD over mDNS, UDP broadcast and static peers, as pure codecs with thin socket shells and no library. A discovered address is a hint, never trusted: only the TLS handshake proves who answers.

Namespace: `Axiom.Mesh.Discovery`. Depends on `Axiom.Mesh.Model` for the ids; usable alone.

**A building block of Core and Local, not referenced directly.** It is published on nuget.org as Core's dependency (one
version for every Axiom.Mesh package); a node gets discovery through `Axiom.Mesh.Core`. It stays usable on its own for a
tool that only wants to see which nodes announce themselves on a LAN.

## Quick Start

```csharp
using System.Net;
using Axiom.Mesh;
using Axiom.Mesh.Discovery;

// The ids come from the node's keys (PublicKey.AsDeviceId / AsUserId); any 32 bytes do here.
var self = new SelfRecord
{
    Device = new DeviceId(Hash32.Of("laptop"u8)),
    User = new UserId(Hash32.Of("alice"u8)),
    Port = 41810,
};
var facts = new DiscoveryFacts(capacity: 64);

await using MdnsDiscovery mdns = MdnsDiscovery.Open(new MdnsDiscoveryOptions { Self = self, Time = TimeProvider.System, Facts = facts });
await using BroadcastDiscovery broadcast = BroadcastDiscovery.Open(new BroadcastDiscoveryOptions { Self = self, Time = TimeProvider.System, Facts = facts });
var statics = new StaticPeers(new StaticPeersOptions { Peers = [new DnsEndPoint("nas.local", 41810)], Time = TimeProvider.System, Facts = facts });

// One stream, each claim once: the cache keeps the newest claim per device and bounds what spam can cost.
var discovery = new CompositeDiscovery(new CompositeDiscoveryOptions
{
    Parts = [mdns, broadcast, statics],
    Time = TimeProvider.System,
    Cache = new AnnouncementCache(capacity: 256, repeatAfter: TimeSpan.FromMinutes(5)),
});

using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(1));
try
{
    await foreach (Announcement heard in discovery.ObserveAsync(stop.Token))
    {
        // A hint: dial a device only when a roster you hold lists it (SPEC.md §3); for a Static peer the handshake decides.
        Console.WriteLine($"{heard.Source}: {heard.Device} of {heard.User} at {heard.Address}");
    }
}
catch (OperationCanceledException)
{
}

// What could not be done (a port taken, an interface down, a name unresolved) is a fact, never an exception.
while (facts.Reader.TryRead(out DiscoveryFact? fact))
    Console.WriteLine(fact);
```

The codecs are pure and can be used without a socket:

```csharp
using System.Net;
using Axiom.Mesh;
using Axiom.Mesh.Discovery;

var node = new SelfRecord { Device = new DeviceId(Hash32.Of("pc"u8)), User = new UserId(Hash32.Of("bob"u8)), Port = 41810 };

byte[] datagram = BroadcastCodec.Write(BroadcastKind.Announcement, node);
if (BroadcastCodec.TryParse(datagram, out BroadcastDatagram read))
    Console.WriteLine(read.ToAnnouncement(IPAddress.Parse("192.168.1.20")));

byte[] answer = DnsSdCodec.WriteAnswer(node, [IPAddress.Parse("192.168.1.20")]);
foreach (Announcement claim in DnsSdCodec.ReadAnnouncements(answer, IPAddress.Parse("192.168.1.20")))
    Console.WriteLine($"{DnsSdCodec.InstanceName(claim.Device)}._axiom-mesh._tcp.local -> {claim.Address}");

// SPEC.md §3.2: at most one announcement per second per interface; timestamps in, no clock.
var gate = new AnnounceGate();
Console.WriteLine(gate.TryPass(TimeSpan.Zero, interfaceIndex: 2));                    // True
Console.WriteLine(gate.TryPass(TimeSpan.FromMilliseconds(500), interfaceIndex: 2));   // False
```

## What is in it

| Folder | Types |
|---|---|
| root | `Announcement` (device, user, address, source), `DiscoverySource`, `SelfRecord`, `IDiscovery`, `DiscoveryFacts` / `DiscoveryFact` / `DiscoveryFactKind` |
| `Codecs/` | `DnsSdCodec` (the mDNS answer and query, reading a received packet), `BroadcastCodec` (the 71-byte `AMD1` datagram), `BroadcastDatagram`, `BroadcastKind` |
| `Rules/` | `AnnounceGate` (≤ 1 announcement per second per interface), `AnnouncementCache` (bounded, newest claim per device wins), `AnnouncementNovelty` |
| `Shells/` | `MdnsDiscovery` (UDP 5353, 224.0.0.251 and ff02::fb per interface), `BroadcastDiscovery` (UDP 41811, directed broadcast per interface, ff02::1 on IPv6), `StaticPeers`, `CompositeDiscovery`, with their options |

## Rules of use

- **A claim is a hint.** Never dial an announced device that is not, in a roster you hold, a device of a known user
  (SPEC.md §3). Decide on `Source`: only `Static` may be dialled without a known id, and no parser yields an all-zero
  id, so nothing from the network can pose as a static peer.
- **Nothing on the network throws.** The parsers are bounded (9000 bytes, 64 records, names of 255, 16 compression
  jumps, 71 bytes exactly) and return nothing for anything malformed. A shell that cannot bind, join or send records a
  `DiscoveryFact` and carries on; logging is the engine's.
- **Announcements are gated.** Every announcement, an answer to a query included, passes the `AnnounceGate`, so a flood
  of queries costs at most one answer per second per interface.
- **Shells are owned.** `Open` binds and starts; `DisposeAsync` stops, closes the sockets and ends every observer's
  stream. A shell has any number of observers; a `CompositeDiscovery` has one, because its cache decides what is new.
- **mDNS coexists.** `MdnsDiscovery` binds 5353 with ReuseAddress beside Bonjour, Avahi or the Windows DNS client, and
  names its host `<instance>.local`, never the machine's name. It is verified by hand: multicast is filtered on CI
  (STRUCTURAL-DEBT.md entry 2).
