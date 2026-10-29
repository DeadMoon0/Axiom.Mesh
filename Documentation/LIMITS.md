# Limits

Every bound Axiom.Mesh enforces, with the value its specification states, where it is stated, and what happens when
it is reached. The values are fixed by AMP/1 ([SPEC.md](SPEC.md)) and `axiom-mesh-local/1`
([LOCAL-SPEC.md](LOCAL-SPEC.md)); they are not configuration. Each is a named constant of `Axiom.Mesh.Limits` with
the specification's value (ARCHITECTURE.md rule 5); the **Constant** column names it. `LimitsDocumentTests` (in
`Model.Tests`) fails when a row names a constant `Limits` lacks, when `Limits` holds a constant no row names, or when a
row names none; `LimitsTests` checks every value against the section that states it.

## AMP/1, the mesh (SPEC.md)

| Limit | Value | § | When reached | Constant |
|---|---|---|---|---|
| frame length | 2 to 4 198 400 bytes (4 MiB + 4 KiB) | 5.1 | violation, `Bye(Violation)` | `Limits.MinFrameLength`, `Limits.MaxFrameLength` |
| frame completion | 30 s from the first byte | 5.1 | `Bye(Violation)` | `Limits.FrameCompletionSeconds` |
| TLS handshake | 10 s from the TCP connection | 4.2 | connection closed | `Limits.TlsHandshakeSeconds` |
| first `Hello` | 10 s | 6.1 | connection closed | `Limits.FirstHelloSeconds` |
| `Ping` after silence | 60 s without sending | 6.3 | a `Ping` is sent | `Limits.PingAfterSilenceSeconds` |
| idle | 5 min without receiving | 6.3 | `Bye(Idle)` | `Limits.IdleSeconds` |
| redial after a failure | 10 s, doubling to 5 min | 4.4 | not dialled before then | `Limits.RedialFirstSeconds`, `Limits.RedialMaxSeconds` |
| violations per device | 3 within an hour | 12.1 | not dialled for an hour | `Limits.ViolationsPerWindow`, `Limits.ViolationWindowSeconds` |
| sessions per device id | 1 | 4.5 | the duplicate rule closes one with `Bye(Duplicate)` | `Limits.SessionsPerDevice` |
| dead duplicate session | 60 s without receiving | 4.5 | the new session is kept | `Limits.DeadSessionSeconds` |
| admission per peer address | small, such as 4 (SHOULD) | 4.6 | refused before a byte | `Limits.AdmissionPerAddress` |
| `Hello.services` | 64 names of at most 64 bytes | 7.1 | violation | `Limits.MaxHelloServices`, `Limits.MaxServiceNameLength` |
| listing | 64 per page; 65 536 in all; complete within 60 s of page 0 | 7.2 | violation | `Limits.MaxPackagesPerPage`, `Limits.MaxListingPackages`, `Limits.ListingSeconds` |
| `PackageState.grants`, `.heads` | 256 each | 7.2 | violation | `Limits.MaxPackageGrants`, `Limits.MaxPackageHeads` |
| `Have`, `Cancel` | 1 024 hashes | 7.4, 7.8 | violation | `Limits.MaxHaveHashes`, `Limits.MaxCancelHashes` |
| recorded sources | 65 536 hashes per peer | 7.4 | oldest dropped | `Limits.MaxRecordedSources` |
| `Want` | 64 hashes; 64 outstanding per peer | 7.5 | violation | `Limits.MaxWantHashes`, `Limits.MaxOutstandingWants` |
| queued `Object` payload | 8 MiB toward one peer | 7.6 | further `Want`s deferred; `Ping`, `Pong`, `Cancel` still read | `Limits.MaxQueuedObjectBytes` |
| `Bye.detail` | 256 bytes | 7.13 | violation | `Limits.MaxByeDetailLength` |
| service names | 64 bytes | 7.10 | violation | `Limits.MaxServiceNameLength` |
| object | 4 MiB (4 194 304 bytes) | 8.1 | malformed | `Limits.MaxObjectLength` |
| Chunk data | 1 to 1 048 576 bytes; exactly 1 MiB but the last of a file | 8.2 | malformed | `Limits.MinChunkLength`, `Limits.ChunkLength` |
| ChunkList | 32 768 chunks (32 GiB per file) | 8.3 | malformed | `Limits.MaxChunkListChunks` |
| Tree | 8 192 entries; names of at most 255 bytes | 8.4 | malformed | `Limits.MaxTreeEntries`, `Limits.MaxTreeNameLength` |
| Version payload | label 64, prerelease 64, notes 4 096 bytes | 8.5 | malformed | `Limits.MaxVersionLabelLength`, `Limits.MaxVersionPrereleaseLength`, `Limits.MaxVersionNotesLength` |
| Message payload | text 16 384 bytes; 64 attachments, names of 255 bytes | 8.5 | malformed | `Limits.MaxMessageTextLength`, `Limits.MaxMessageAttachments`, `Limits.MaxAttachmentNameLength` |
| Entry | 2 parents; payload 1 MiB; clock below 2^63; gap at most 2^32 | 8.6 | malformed, or faulty for the gap | `Limits.MaxEntryParents`, `Limits.MaxPayloadLength`, `Limits.ClockLimit`, `Limits.MaxClockGap` |
| Manifest | name 255 bytes; 64 branches, names of 64 bytes | 8.7 | malformed | `Limits.MaxManifestNameLength`, `Limits.MaxManifestBranches`, `Limits.MaxBranchNameLength` |
| Roster | 256 devices; names of 64 bytes | 8.8 | malformed | `Limits.MaxRosterDevices`, `Limits.MaxDeviceNameLength` |
| held buffer | 8 MiB per session, 64 MiB in all; 10 min after the sender left | 10.6 | oldest dropped | `Limits.MaxHeldBytesPerSession`, `Limits.MaxHeldBytes`, `Limits.HeldAfterDisconnectSeconds` |
| re-ask a peer | 30 s without an answer | 10.3 | asked again elsewhere | `Limits.ReaskSeconds` |

## axiom-mesh-local/1, the pipe (LOCAL-SPEC.md §12)

| Limit | Value | When reached | Constant |
|---|---|---|---|
| frame length | 2 to 4 198 400 bytes | violation | `Limits.MinFrameLength`, `Limits.MaxFrameLength` |
| frame completion | 30 s from the first byte | violation | `Limits.FrameCompletionSeconds` |
| `Hello` after connecting | 10 s | close without a frame | `Limits.LocalHelloSeconds` |
| connections waiting for `Hello` | 16 | the longest-waiting closed | `Limits.MaxConnectionsWaitingForHello` |
| `Pong` after `Ping` | 30 s; `Ping` after 60 s without receiving | `Bye(Unresponsive)` | `Limits.LocalPongSeconds`, `Limits.LocalPingAfterSeconds` |
| outstanding requests per connection | 64 | violation | `Limits.MaxOutstandingRequests` |
| outstanding callbacks per connection | 64 | `Busy` (§9.8) | `Limits.MaxOutstandingCallbacks` |
| subscriptions per connection | 64 | `Busy` | `Limits.MaxSubscriptions` |
| queued events per subscription | 256 events or 16 MiB | lapse (§7.4) | `Limits.MaxQueuedEvents`, `Limits.MaxQueuedEventBytes` |
| replay per `Subscribe` | 256 Entries | `GapTooLarge` | `Limits.MaxReplayEntries` |
| frames handed to the pipe, not yet accepted | 64 MiB per connection, 256 MiB per Host, or 30 s for one frame | `Bye(Unresponsive)` | `Limits.MaxUnacceptedBytesPerConnection`, `Limits.MaxUnacceptedBytesPerHost`, `Limits.UnacceptedFrameSeconds` |
| running operations per connection | 16 | `Busy` | `Limits.MaxRunningOperations` |
| ended operations kept per connection | 48 | oldest dropped | `Limits.MaxEndedOperations` |
| user connections | 64 | `Bye(Refused, "busy")` | `Limits.MaxUserConnections` |
| service connections | 1 per configured name | `Bye(Duplicate)` | `Limits.ServiceConnectionsPerName` |
| pairings per Host | 1 at a time, 10 minutes | `Busy` / `Failed` | `Limits.PairingsPerHost`, `Limits.PairingSeconds` |
| static peers | 256 | `Busy` | `Limits.MaxStaticPeers` |
| `Drain` grace | 5 000 ms default, 30 000 ms at most | `Bye(Shutdown)` | `Limits.DrainGraceDefaultMilliseconds`, `Limits.DrainGraceMaxMilliseconds` |
| `ServiceCall` / `ForwardedCall` | 60 s | `ServiceTimeout` | `Limits.ServiceCallSeconds` |
| `Fetch` without a source | 60 s | `NotFound` (§6.4) | `Limits.FetchWithoutSourceSeconds` |
| pinned bytes | the store budget | `StoreFull` (§6.5) | — |
| page of `ListHistory`, `ListStored` | 64 Entries, cut to the frame | — | `Limits.HistoryPageEntries` |
| page of `ListPackages`, `ListPins` | 256, cut to the frame | — | `Limits.PackagePageEntries` |
| lists without a cursor | their maximum, cut to the frame (§6) | — | — |
| points per `QueryTelemetry` | 10 080 (7 days) | `Invalid` | `Limits.MaxTelemetryPoints` |
| telemetry retention | 7 days of minutes; totals for ever | dropped | `Limits.TelemetryRetentionDays` |
| service names | 32 bytes (§3.2) | `Bye(Refused, "service")` | `Limits.MaxLocalServiceNameLength` |
| service tokens | 32 bytes (§4.2) | `Bye(Refused, "service")` | `Limits.ServiceTokenLength` |

Two rows name no constant, because the specification fixes no number: pinned bytes are bounded by the store budget,
which is configuration, and a list without a cursor is bounded by that list's own maximum.
