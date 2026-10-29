namespace Axiom.Mesh.Discovery;

/// <summary>Bit 0 of a broadcast datagram's flags (SPEC.md §3.2).</summary>
public enum BroadcastKind
{
    /// <summary>Bit 0 clear: asks every listener to answer with an announcement, sent unicast to the query's source.</summary>
    Query = 0,

    /// <summary>Bit 0 set: the sender announces itself.</summary>
    Announcement = 1,
}
