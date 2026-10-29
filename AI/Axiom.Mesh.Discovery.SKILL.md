<identity>
    <package>Axiom.Mesh.Discovery</package>
    <role>addon-skill</role>
    <requires>Axiom.Mesh.Model.SKILL.md</requires>
</identity>

<objective>
    Teach an agent how Axiom.Mesh finds nodes on a LAN, and that a discovered address is only a hint.
</objective>

<package_scope>
    Namespace Axiom.Mesh.Discovery (folders for orientation only).
    Root: Announcement(Device, User, Address: IPEndPoint, Source) [ClosedShape] with ForStaticPeer(address) (all-zero ids) and ClaimsTheSameAs; DiscoverySource flags (None, Mdns, Broadcast, Static); SelfRecord { Device, User, Port } (non-zero ids, port not 0); IDiscovery { ObserveAsync(ct) : IAsyncEnumerable&lt;Announcement&gt;; AnnounceAsync(ct) }; DiscoveryFacts (bounded sink, Record, Reader) with DiscoveryFact { At, Mechanism, Kind, Subject, Detail } and DiscoveryFactKind.
    Codecs (pure): DnsSdCodec (ServiceType _axiom-mesh._tcp, InstanceName(device) = first 32 hex, WriteQuery, WriteAnswer(self, addresses), IsServiceQuery(packet), ReadAnnouncements(packet, source); bounds MaxPacketLength 9000, MaxRecords 64, MaxInstances 8, MaxAddresses 16); BroadcastCodec (Port 41811, Length 71, Magic "AMD1", TryParse, Write(kind, self[, span])); BroadcastDatagram(Kind, Device, User, Port).ToAnnouncement(source); BroadcastKind (Query 0, Announcement 1).
    Rules (pure, timestamps in): AnnounceGate.TryPass(timestamp, interfaceIndex) (at most 1 per second per interface); AnnouncementCache(capacity, repeatAfter).Observe(announcement, timestamp) → AnnouncementNovelty (Duplicate, New, Changed, Repeated), TryGet(device), Snapshot().
    Shells (dishonest, thin, BCL sockets): MdnsDiscovery.Open(MdnsDiscoveryOptions), BroadcastDiscovery.Open(BroadcastDiscoveryOptions) (both IAsyncDisposable, QueryAsync, periodic announce on AnnounceInterval), StaticPeers(StaticPeersOptions) (IPEndPoint or DnsEndPoint, offered every Interval), CompositeDiscovery(CompositeDiscoveryOptions { Parts, Time, Cache }).
</package_scope>

<architecture>
    Pure codecs and rules under thin socket shells, no library (DESIGN.md 2). Depends on Model for DeviceId, UserId and Hash32; usable alone. Shells take a TimeProvider and a DiscoveryFacts sink and log nothing: what fails is a fact for the engine to log. Each observer of a shell has its own bounded queue (256, oldest dropped). The broadcast shell answers a query unicast to its source on the socket it arrived on; the mDNS shell answers a service query by multicast on the interface it arrived on; both pass every announcement through their AnnounceGate.
</architecture>

<best_practices>
    Merge mechanisms with one CompositeDiscovery and keep its AnnouncementCache: when a user becomes known (pairing), look up its devices there instead of waiting for the next announcement. Dial only devices a held roster lists; dial Source Static addresses and let the handshake decide. Read DiscoveryFacts.Reader and log each fact once. Test against the pure codecs and rules; use a fake IDiscovery in engine tests, never sockets. Run BroadcastDiscovery on 127.0.0.1 with Port 0 and AnnounceInterval Timeout.InfiniteTimeSpan for a loopback test.
</best_practices>

<pitfalls>
    Never dial a discovered device that is not a device of a known user (SPEC.md 3); never decide "static" by all-zero ids, decide by Source. A CompositeDiscovery takes one observer at a time (its cache decides what is new); a second concurrent ObserveAsync throws InvalidOperationException. Cancelling ObserveAsync ends it with OperationCanceledException. IPEndPoint is mutable: do not change an Announcement's Address. mDNS cannot run on CI runners; it is checked by hand (STRUCTURAL-DEBT.md entry 2). IPv6 has no broadcast: the broadcast shell sends to ff02::1 per interface instead.
</pitfalls>
