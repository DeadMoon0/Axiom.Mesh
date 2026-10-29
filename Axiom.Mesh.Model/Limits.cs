namespace Axiom.Mesh;

/// <summary>
/// Every bound of AMP/1 (<c>SPEC.md</c>) and of <c>axiom-mesh-local/1</c> (<c>LOCAL-SPEC.md</c>) as a named constant
/// with the specification's value, so that a bound is written once and enforced where the bytes enter.
/// </summary>
/// <remarks>
/// A constant is compiled into every caller, so its value can never change within a protocol version; the
/// specifications fix them. <c>Documentation/LIMITS.md</c> names the constant of every row, and a convention test
/// compares the two in both directions. Durations are whole seconds (or milliseconds where the specification counts
/// them), sizes are bytes, counts are elements.
/// </remarks>
public static class Limits
{
    // ---- AMP/1 framing and session (SPEC.md §4–§7) ----

    /// <summary>The smallest frame length, the two bytes of the message id (SPEC.md §5.1, LOCAL-SPEC.md §12).</summary>
    public const int MinFrameLength = 2;

    /// <summary>The largest frame length, 4 MiB + 4 KiB (SPEC.md §5.1, LOCAL-SPEC.md §12).</summary>
    public const int MaxFrameLength = 4_198_400;

    /// <summary>Seconds within which a frame must be received completely after its first byte (SPEC.md §5.1, LOCAL-SPEC.md §12).</summary>
    public const int FrameCompletionSeconds = 30;

    /// <summary>Seconds within which the TLS handshake must complete after the TCP connection (SPEC.md §4.2).</summary>
    public const int TlsHandshakeSeconds = 10;

    /// <summary>Seconds within which the peer's <c>Hello</c> must arrive (SPEC.md §6.1).</summary>
    public const int FirstHelloSeconds = 10;

    /// <summary>Seconds without sending after which a node sends <c>Ping</c> (SPEC.md §6.3).</summary>
    public const int PingAfterSilenceSeconds = 60;

    /// <summary>Seconds without receiving after which a session is closed with <c>Bye(Idle)</c>, 5 minutes (SPEC.md §6.3).</summary>
    public const int IdleSeconds = 300;

    /// <summary>Seconds before the same address is dialled again after a failure (SPEC.md §4.4).</summary>
    public const int RedialFirstSeconds = 10;

    /// <summary>Seconds the redial backoff doubles up to, 5 minutes (SPEC.md §4.4).</summary>
    public const int RedialMaxSeconds = 300;

    /// <summary>Violations within <see cref="ViolationWindowSeconds"/> after which a device is not dialled (SPEC.md §12.1).</summary>
    public const int ViolationsPerWindow = 3;

    /// <summary>The window violations are counted in, and how long a device is then not dialled: one hour (SPEC.md §12.1).</summary>
    public const int ViolationWindowSeconds = 3_600;

    /// <summary>Established sessions per device id (SPEC.md §4.5).</summary>
    public const int SessionsPerDevice = 1;

    /// <summary>Seconds without receiving after which the older of two sessions to one device counts as dead (SPEC.md §4.5).</summary>
    public const int DeadSessionSeconds = 60;

    /// <summary>Connections admitted per peer address; SPEC.md §4.6 recommends a small number such as 4.</summary>
    public const int AdmissionPerAddress = 4;

    /// <summary>Service names in a <c>Hello</c> (SPEC.md §7.1).</summary>
    public const int MaxHelloServices = 64;

    /// <summary>Bytes of a service protocol name (SPEC.md §7.1, §7.10).</summary>
    public const int MaxServiceNameLength = 64;

    /// <summary><c>PackageState</c>s per <c>Packages</c> page (SPEC.md §7.2).</summary>
    public const int MaxPackagesPerPage = 64;

    /// <summary><c>PackageState</c>s in one listing (SPEC.md §7.2).</summary>
    public const int MaxListingPackages = 65_536;

    /// <summary>Seconds within which a listing must complete after its page 0 (SPEC.md §7.2).</summary>
    public const int ListingSeconds = 60;

    /// <summary>Grant hashes in a <c>PackageState</c> (SPEC.md §7.2).</summary>
    public const int MaxPackageGrants = 256;

    /// <summary>Heads in a <c>PackageState</c> (SPEC.md §7.2).</summary>
    public const int MaxPackageHeads = 256;

    /// <summary>Hashes in a <c>Have</c> (SPEC.md §7.4).</summary>
    public const int MaxHaveHashes = 1_024;

    /// <summary>Hashes in a <c>Cancel</c> (SPEC.md §7.8).</summary>
    public const int MaxCancelHashes = 1_024;

    /// <summary>Hashes whose sources a node records per peer, the oldest dropped beyond (SPEC.md §7.4).</summary>
    public const int MaxRecordedSources = 65_536;

    /// <summary>Hashes in a <c>Want</c> (SPEC.md §7.5).</summary>
    public const int MaxWantHashes = 64;

    /// <summary>Hashes wanted and not yet answered or cancelled toward one peer (SPEC.md §7.5).</summary>
    public const int MaxOutstandingWants = 64;

    /// <summary>Bytes of <c>Object</c> payload queued toward one peer, 8 MiB (SPEC.md §7.6).</summary>
    public const int MaxQueuedObjectBytes = 8 * 1_048_576;

    /// <summary>Bytes of <c>Bye.detail</c> (SPEC.md §7.13).</summary>
    public const int MaxByeDetailLength = 256;

    // ---- AMP/1 objects (SPEC.md §8) ----

    /// <summary>The largest object, 4 MiB (SPEC.md §8.1).</summary>
    public const int MaxObjectLength = 4_194_304;

    /// <summary>The largest Chunk's data, 1 MiB; every Chunk of a file but its last holds exactly this much (SPEC.md §8.2).</summary>
    public const int ChunkLength = 1_048_576;

    /// <summary>The smallest Chunk's data (SPEC.md §8.2).</summary>
    public const int MinChunkLength = 1;

    /// <summary>Chunks of one ChunkList, so 32 GiB per file (SPEC.md §8.3).</summary>
    public const int MaxChunkListChunks = 32_768;

    /// <summary>Entries of one Tree (SPEC.md §8.4).</summary>
    public const int MaxTreeEntries = 8_192;

    /// <summary>Bytes of a Tree entry's name (SPEC.md §8.4).</summary>
    public const int MaxTreeNameLength = 255;

    /// <summary>Bytes of a <c>Version</c>'s label (SPEC.md §8.5).</summary>
    public const int MaxVersionLabelLength = 64;

    /// <summary>Bytes of a <c>Version</c>'s pre-release tag (SPEC.md §8.5).</summary>
    public const int MaxVersionPrereleaseLength = 64;

    /// <summary>Bytes of a <c>Version</c>'s notes (SPEC.md §8.5).</summary>
    public const int MaxVersionNotesLength = 4_096;

    /// <summary>Bytes of a <c>Message</c>'s text (SPEC.md §8.5).</summary>
    public const int MaxMessageTextLength = 16_384;

    /// <summary>Attachments of one <c>Message</c> (SPEC.md §8.5).</summary>
    public const int MaxMessageAttachments = 64;

    /// <summary>Bytes of an attachment's name (SPEC.md §8.5).</summary>
    public const int MaxAttachmentNameLength = 255;

    /// <summary>Parents of one Entry (SPEC.md §8.6).</summary>
    public const int MaxEntryParents = 2;

    /// <summary>Bytes of an Entry's payload, 1 MiB (SPEC.md §8.6; LOCAL-SPEC.md §3.2 <c>payload</c>).</summary>
    public const int MaxPayloadLength = 1_048_576;

    /// <summary>The first clock that is not well-formed, 2^63: every clock is below it (SPEC.md §8.6).</summary>
    public const ulong ClockLimit = 1UL << 63;

    /// <summary>How far an Entry's clock may exceed its greatest parent's, 2^32; more is faulty (SPEC.md §8.6).</summary>
    public const ulong MaxClockGap = 1UL << 32;

    /// <summary>Bytes of a Manifest's name (SPEC.md §8.7).</summary>
    public const int MaxManifestNameLength = 255;

    /// <summary>Branches of one Manifest (SPEC.md §8.7).</summary>
    public const int MaxManifestBranches = 64;

    /// <summary>Bytes of a branch name (SPEC.md §8.7).</summary>
    public const int MaxBranchNameLength = 64;

    /// <summary>Devices of one Roster (SPEC.md §8.8).</summary>
    public const int MaxRosterDevices = 256;

    /// <summary>Bytes of a device's name (SPEC.md §8.8).</summary>
    public const int MaxDeviceNameLength = 64;

    // ---- AMP/1 replication (SPEC.md §10) ----

    /// <summary>Bytes of unverified objects held per session, 8 MiB (SPEC.md §10.6).</summary>
    public const int MaxHeldBytesPerSession = 8 * 1_048_576;

    /// <summary>Bytes of unverified objects held in all, 64 MiB (SPEC.md §10.6).</summary>
    public const int MaxHeldBytes = 64 * 1_048_576;

    /// <summary>Seconds an unverifiable object is held after its sender disconnected, 10 minutes (SPEC.md §10.6).</summary>
    public const int HeldAfterDisconnectSeconds = 600;

    /// <summary>Seconds after which an unanswered <c>Want</c> may be asked of another peer too (SPEC.md §10.3).</summary>
    public const int ReaskSeconds = 30;

    // ---- axiom-mesh-local/1 (LOCAL-SPEC.md §3, §4, §12) ----

    /// <summary>Seconds within which a client's <c>Hello</c> must arrive after connecting (LOCAL-SPEC.md §12).</summary>
    public const int LocalHelloSeconds = 10;

    /// <summary>Connections waiting for their <c>Hello</c>; the longest-waiting is closed beyond (LOCAL-SPEC.md §12).</summary>
    public const int MaxConnectionsWaitingForHello = 16;

    /// <summary>Seconds without receiving after which the Host sends <c>Ping</c> to a client (LOCAL-SPEC.md §12).</summary>
    public const int LocalPingAfterSeconds = 60;

    /// <summary>Seconds within which a client must answer <c>Ping</c>, else <c>Bye(Unresponsive)</c> (LOCAL-SPEC.md §12).</summary>
    public const int LocalPongSeconds = 30;

    /// <summary>Requests outstanding per connection (LOCAL-SPEC.md §12).</summary>
    public const int MaxOutstandingRequests = 64;

    /// <summary>Callbacks outstanding per connection (LOCAL-SPEC.md §12).</summary>
    public const int MaxOutstandingCallbacks = 64;

    /// <summary>Subscriptions per connection (LOCAL-SPEC.md §12).</summary>
    public const int MaxSubscriptions = 64;

    /// <summary>Events queued per subscription before it lapses (LOCAL-SPEC.md §7.4, §12).</summary>
    public const int MaxQueuedEvents = 256;

    /// <summary>Bytes of events queued per subscription before it lapses, 16 MiB (LOCAL-SPEC.md §7.4, §12).</summary>
    public const int MaxQueuedEventBytes = 16 * 1_048_576;

    /// <summary>Entries one <c>Subscribe</c> replays; more is <c>GapTooLarge</c> (LOCAL-SPEC.md §12).</summary>
    public const int MaxReplayEntries = 256;

    /// <summary>Bytes handed to the pipe and not yet accepted per connection, 64 MiB (LOCAL-SPEC.md §12).</summary>
    public const int MaxUnacceptedBytesPerConnection = 64 * 1_048_576;

    /// <summary>Bytes handed to the pipe and not yet accepted per Host, 256 MiB (LOCAL-SPEC.md §12).</summary>
    public const int MaxUnacceptedBytesPerHost = 256 * 1_048_576;

    /// <summary>Seconds one frame may wait to be accepted by the pipe (LOCAL-SPEC.md §12).</summary>
    public const int UnacceptedFrameSeconds = 30;

    /// <summary>Operations running per connection (LOCAL-SPEC.md §12).</summary>
    public const int MaxRunningOperations = 16;

    /// <summary>Ended operations kept per connection, the oldest dropped beyond (LOCAL-SPEC.md §12).</summary>
    public const int MaxEndedOperations = 48;

    /// <summary>User connections per Host (LOCAL-SPEC.md §12).</summary>
    public const int MaxUserConnections = 64;

    /// <summary>Service connections per configured service name (LOCAL-SPEC.md §12).</summary>
    public const int ServiceConnectionsPerName = 1;

    /// <summary>Pairings running at a time per Host (LOCAL-SPEC.md §12).</summary>
    public const int PairingsPerHost = 1;

    /// <summary>Seconds a pairing may take, 10 minutes (LOCAL-SPEC.md §12).</summary>
    public const int PairingSeconds = 600;

    /// <summary>Static peers per Host (LOCAL-SPEC.md §12).</summary>
    public const int MaxStaticPeers = 256;

    /// <summary>The <c>Drain</c> grace when the request names none, in milliseconds (LOCAL-SPEC.md §12).</summary>
    public const int DrainGraceDefaultMilliseconds = 5_000;

    /// <summary>The longest <c>Drain</c> grace, in milliseconds (LOCAL-SPEC.md §12).</summary>
    public const int DrainGraceMaxMilliseconds = 30_000;

    /// <summary>Seconds a <c>ServiceCall</c> or <c>ForwardedCall</c> may take (LOCAL-SPEC.md §12).</summary>
    public const int ServiceCallSeconds = 60;

    /// <summary>Seconds a <c>Fetch</c> waits for a source before it answers <c>NotFound</c> (LOCAL-SPEC.md §12).</summary>
    public const int FetchWithoutSourceSeconds = 60;

    /// <summary>Entries per page of <c>ListHistory</c> and <c>ListStored</c>, cut to the frame (LOCAL-SPEC.md §12).</summary>
    public const int HistoryPageEntries = 64;

    /// <summary>Items per page of <c>ListPackages</c> and <c>ListPins</c>, cut to the frame (LOCAL-SPEC.md §12).</summary>
    public const int PackagePageEntries = 256;

    /// <summary>Points per <c>QueryTelemetry</c>, 7 days of minutes (LOCAL-SPEC.md §12).</summary>
    public const int MaxTelemetryPoints = 10_080;

    /// <summary>Days of per-minute telemetry kept (LOCAL-SPEC.md §12).</summary>
    public const int TelemetryRetentionDays = 7;

    /// <summary>Bytes of a local service name (LOCAL-SPEC.md §3.2, §4.2).</summary>
    public const int MaxLocalServiceNameLength = 32;

    /// <summary>Bytes of a spawned or external service's token (LOCAL-SPEC.md §4.2).</summary>
    public const int ServiceTokenLength = 32;
}
