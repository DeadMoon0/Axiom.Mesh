namespace Axiom.Mesh.Local;

/// <summary>
/// The bounds of the fields of LOCAL-SPEC.md §4, §5, §6, §7, §9 and §10 that are not limits of §12: the maximum of each
/// <c>string(max)</c>, <c>blob(max)</c> and <c>list&lt;T&gt;(max)</c> as its layout states it. A value outside one is a
/// violation in a request or a <c>Reply</c> and a Host defect in a <c>Response</c>, <c>Event</c> or <c>Callback</c>
/// (§3.2). Where a bound equals a limit of <see cref="Limits"/>, the limit is used, so it is stated once.
/// </summary>
internal static class FieldBounds
{
    /// <summary>A <c>name</c> (§3.2): a client, service, method, event or series name.</summary>
    public const int NameLength = 64;

    /// <summary><c>Hello.clientVersion</c>, <c>HelloAck.hostVersion</c>, <c>Service.version</c>.</summary>
    public const int VersionLength = 64;

    /// <summary><c>Bye.detail</c> (§4.3).</summary>
    public const int ByeDetailLength = 256;

    /// <summary>A user's display name (§5.1, §5.2, §6.1).</summary>
    public const int UserNameLength = 64;

    /// <summary>A device's name (§5.1, §5.3), as in a Roster.</summary>
    public const int DeviceNameLength = Limits.MaxDeviceNameLength;

    /// <summary>A Package's name (§5.4, §6.4), as in a Manifest.</summary>
    public const int PackageNameLength = Limits.MaxManifestNameLength;

    /// <summary>A <c>path</c> (§3.2).</summary>
    public const int PathLength = 4_096;

    /// <summary><c>Pairing.code</c> and <c>StartPairing.code</c> (§5.15, §6.2).</summary>
    public const int PairingCodeLength = 512;

    /// <summary><c>Pairing.sas</c> (§5.15).</summary>
    public const int SasLength = 64;

    /// <summary><c>Pairing.failure</c> (§5.15).</summary>
    public const int FailureLength = 256;

    /// <summary>An address <c>host:port</c> (§5.7, §5.8, §5.9, §6.2, §6.6).</summary>
    public const int AddressLength = 256;

    /// <summary><c>Session.tlsSuite</c> (§5.8).</summary>
    public const int TlsSuiteLength = 64;

    /// <summary><c>Session.service</c>, the service protocol name while Switched (§5.8).</summary>
    public const int ServiceProtocolLength = 64;

    /// <summary><c>Operation.message</c> (§5.13).</summary>
    public const int OperationMessageLength = 256;

    /// <summary><c>Operation.result</c> (§5.13).</summary>
    public const int OperationResultLength = 4_096;

    /// <summary><c>Error.message</c> (§5.14).</summary>
    public const int ErrorMessageLength = 256;

    /// <summary><c>Error.detail</c> (§5.14).</summary>
    public const int ErrorDetailLength = 1_024;

    /// <summary>A telemetry key (§5.18, §9.9, §10.1).</summary>
    public const int TelemetryKeyLength = 192;

    /// <summary><c>Filter.service</c> and <c>Filter.series</c> (§5.18).</summary>
    public const int FilterTextLength = 64;

    /// <summary>A list of kinds in a request or a filter (§5.18, §6.4, §9.2).</summary>
    public const int KindsCount = 16;

    /// <summary><c>Service.kinds</c> (§5.12).</summary>
    public const int ServiceKindsCount = 16;

    /// <summary><c>Service.entryKinds</c> (§5.12).</summary>
    public const int ServiceEntryKindsCount = 64;

    /// <summary><c>Package.branches</c> and <c>UpdatePackage.branches</c> (§5.4, §6.4), as in a Manifest.</summary>
    public const int BranchesCount = Limits.MaxManifestBranches;

    /// <summary><c>Package.heads</c> (§5.4), as in a <c>PackageState</c>.</summary>
    public const int HeadsCount = Limits.MaxPackageHeads;

    /// <summary><c>Entry.parents</c> and <c>WriteEntry.parents</c> (§5.6, §9.4), as in an Entry.</summary>
    public const int ParentsCount = Limits.MaxEntryParents;

    /// <summary><c>ListKnownUsers</c> (§6.2).</summary>
    public const int KnownUsersCount = 1_024;

    /// <summary><c>ListDevices</c> and <c>UserChanged.devices</c> (§6.3, §7.3), as in a Roster.</summary>
    public const int DevicesCount = Limits.MaxRosterDevices;

    /// <summary>A page of <c>ListPackages</c> (§6.4, §12).</summary>
    public const int PackagesCount = Limits.PackagePageEntries;

    /// <summary><c>ListGrants</c> (§6.4).</summary>
    public const int GrantsCount = 256;

    /// <summary>A page of <c>ListHistory</c> and <c>ListStored</c> (§6.4, §12).</summary>
    public const int EntriesCount = Limits.HistoryPageEntries;

    /// <summary>A page of <c>ListPins</c> (§6.5, §12).</summary>
    public const int PinsCount = Limits.PackagePageEntries;

    /// <summary><c>Unpin.hashes</c> (§6.5).</summary>
    public const int UnpinCount = 1_024;

    /// <summary><c>ListPeers</c> and <c>ListDiscovered</c> (§6.6).</summary>
    public const int PeersCount = 1_024;

    /// <summary><c>ListSessions</c> (§6.6).</summary>
    public const int SessionsCount = 256;

    /// <summary><c>ListStaticPeers</c> (§6.6, §12).</summary>
    public const int StaticPeersCount = Limits.MaxStaticPeers;

    /// <summary><c>ListServices</c> (§6.7).</summary>
    public const int ServicesCount = 64;

    /// <summary><c>ListOperations</c> (§8.5).</summary>
    public const int OperationsCount = 64;

    /// <summary><c>ListSeries</c> (§10.3).</summary>
    public const int SeriesCount = 256;

    /// <summary>A page of <c>ListKeys</c> (§10.3).</summary>
    public const int KeysCount = 1_024;

    /// <summary><c>QueryTelemetry</c> (§10.3, §12).</summary>
    public const int PointsCount = Limits.MaxTelemetryPoints;

    /// <summary><c>WriteEntry.references</c> and <c>Entry.references</c> (§9.4, §5.6), as in SPEC.md §8.6.</summary>
    public const int ReferencesCount = Limits.MaxEntryReferences;

    /// <summary><c>Peer.addresses</c> (§5.7) and <c>StartPairingByDevice.addresses</c> (§6.2).</summary>
    public const int AddressesCount = 8;

    /// <summary>An <c>object</c> (§3.2), as in SPEC.md §8.1.</summary>
    public const int ObjectLength = Limits.MaxObjectLength;

    /// <summary>A <c>payload</c> (§3.2), as in SPEC.md §8.6.</summary>
    public const int PayloadLength = Limits.MaxPayloadLength;
}
