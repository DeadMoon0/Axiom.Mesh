<identity>
    <package>Axiom.Mesh.Discovery</package>
    <role>addon-skill</role>
    <requires>Axiom.Mesh.Model.SKILL.md</requires>
</identity>

<objective>
    Teach an agent how Axiom.Mesh finds nodes on a LAN, and that a discovered address is only a hint.
</objective>

<package_scope>
    Namespace Axiom.Mesh.Discovery. Today: DnsSdCodec.ServiceType. Planned (M4): Announcement, IDiscovery, DnsSdCodec, BroadcastCodec (71 bytes, AMD1, UDP 41811), AnnounceGate, MdnsDiscovery, BroadcastDiscovery, StaticPeers, CompositeDiscovery. Status: phase 1a skeleton; read the package README for what exists today. Every rule cites the section of SPEC.md or LOCAL-SPEC.md it implements, and its tests take expected values from that section.
</package_scope>

<architecture>
    Pure codecs with thin socket shells, no library (DESIGN.md 2). Depends on Model for the ids; usable alone.
</architecture>

<best_practices>
    Keep codecs pure and test them byte for byte; the announce gate takes a timestamp, not a clock.
</best_practices>

<pitfalls>
    Never dial a discovered device that is not a device of a known user (SPEC.md 3). mDNS cannot run on CI runners; it is checked by hand (STRUCTURAL-DEBT.md entry 2).
</pitfalls>
