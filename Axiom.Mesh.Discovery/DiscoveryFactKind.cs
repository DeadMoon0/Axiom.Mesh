namespace Axiom.Mesh.Discovery;

/// <summary>What a discovery shell could not do. Each is skipped, never thrown: discovery is a hint and a node runs without it.</summary>
public enum DiscoveryFactKind
{
    /// <summary>Unspecified: the value of an uninitialised field, never recorded by a shell.</summary>
    Unspecified = 0,

    /// <summary>A socket could not be opened or bound (the port is taken exclusively, the address family is off); that mechanism is silent on that family.</summary>
    SocketUnavailable = 1,

    /// <summary>The network interfaces could not be listed; nothing is announced until they can be.</summary>
    InterfacesUnreadable = 2,

    /// <summary>One interface could not join the multicast group or be sent on; it is skipped and tried again on the next announcement.</summary>
    InterfaceSkipped = 3,

    /// <summary>Receiving on a socket failed for a reason other than one stray datagram; that socket stopped receiving.</summary>
    ReceiveStopped = 4,

    /// <summary>A configured host name of a static peer did not resolve; it is tried again on the next round.</summary>
    NameNotResolved = 5,
}
