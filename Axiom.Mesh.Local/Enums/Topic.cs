namespace Axiom.Mesh.Local;

/// <summary>What a subscription receives (LOCAL-SPEC.md §7.2, §14.5).</summary>
/// <remarks>Topics 11 to 255 are reserved for extensions; a Host answers an unknown topic <c>Invalid</c>.</remarks>
public enum Topic : ushort
{
    /// <summary>Assigned to nothing; <c>SubscriptionLapsed</c> belongs to every topic.</summary>
    Unassigned = 0,

    /// <summary><c>PackageChanged</c>.</summary>
    Packages = 1,

    /// <summary><c>EntryStored</c>, with replay from <c>Filter.FromSeq</c>.</summary>
    Entries = 2,

    /// <summary><c>PeerChanged</c>.</summary>
    Peers = 3,

    /// <summary><c>TransferChanged</c>.</summary>
    Transfers = 4,

    /// <summary><c>OperationChanged</c>.</summary>
    Operations = 5,

    /// <summary><c>PairingChanged</c>.</summary>
    Pairing = 6,

    /// <summary><c>TelemetrySample</c>.</summary>
    Telemetry = 7,

    /// <summary><c>ServiceEvent</c>.</summary>
    Service = 8,

    /// <summary><c>UserChanged</c>.</summary>
    Users = 9,

    /// <summary><c>StoreChanged</c>.</summary>
    Store = 10,
}
