using System;
using System.Reflection;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>Every bound of SPEC.md and LOCAL-SPEC.md §12, with the value the specification states.</summary>
public sealed class LimitsTests
{
    private const int MiB = 1024 * 1024;

    // SPEC.md §5.1: "length MUST be at least 2 and at most 4 198 400 (4 MiB + 4 KiB)"; LOCAL-SPEC.md §12 repeats it.
    [Fact]
    public void AFrameIsAtLeastItsMessageIdAndAtMost4MiBPlus4KiB()
    {
        Assert.Equal(2, Limits.MinFrameLength);
        Assert.Equal((4 * 1024 * 1024) + (4 * 1024), Limits.MaxFrameLength);
    }

    // SPEC.md §8.1: "An object MUST be at most 4 MiB"; §8.2: chunks of "exactly 1 MiB except the last".
    [Fact]
    public void AnObjectIsAtMost4MiB_AndAChunkOfAFileHolds1MiB()
    {
        Assert.Equal(4 * 1024 * 1024, Limits.MaxObjectLength);
        Assert.Equal(1024 * 1024, Limits.ChunkLength);
    }

    // SPEC.md §5.1 and §7.6: a frame carries one Object of the largest size together with its header and hash.
    [Fact]
    public void TheLargestObjectFitsInOneFrame()
    {
        const int MessageId = 2;
        const int Hash = 32;
        const int BlobLength = 4;
        Assert.True(MessageId + Hash + BlobLength + Limits.MaxObjectLength <= Limits.MaxFrameLength);
    }

    // SPEC.md §8.6: "clock MUST be below 2^63 … if clock exceeds the greatest parent clock by more than 2^32".
    [Fact]
    public void AClockIsBelow2Pow63AndJumpsAtMost2Pow32()
    {
        Assert.Equal(9_223_372_036_854_775_808UL, Limits.ClockLimit);
        Assert.Equal(4_294_967_296UL, Limits.MaxClockGap);
    }

    // Each row quotes the bound from the section named in its comment.
    [Theory]
    // SPEC §5.1: a frame "MUST be received completely within 30 seconds of its first byte".
    [InlineData(nameof(Limits.FrameCompletionSeconds), 30)]
    // SPEC §4.2: "The TLS handshake MUST complete within 10 seconds of the TCP connection".
    [InlineData(nameof(Limits.TlsHandshakeSeconds), 10)]
    // SPEC §6.1: "If no Hello arrives within 10 seconds, the connection is closed."
    [InlineData(nameof(Limits.FirstHelloSeconds), 10)]
    // SPEC §6.3: "MUST send Ping when it has sent nothing for 60 seconds".
    [InlineData(nameof(Limits.PingAfterSilenceSeconds), 60)]
    // SPEC §6.3: "nothing was received for 5 minutes MUST be closed with Bye(Idle)".
    [InlineData(nameof(Limits.IdleSeconds), 5 * 60)]
    // SPEC §4.4: "not … more often than once per 10 seconds after a failure".
    [InlineData(nameof(Limits.RedialFirstSeconds), 10)]
    // SPEC §4.4: "with exponential backoff to at most 5 minutes".
    [InlineData(nameof(Limits.RedialMaxSeconds), 5 * 60)]
    // SPEC §12.1: "three violations within an hour".
    [InlineData(nameof(Limits.ViolationsPerWindow), 3)]
    // SPEC §12.1: "SHOULD NOT be dialled for an hour".
    [InlineData(nameof(Limits.ViolationWindowSeconds), 60 * 60)]
    // SPEC §4.5: "at most one established session per device id".
    [InlineData(nameof(Limits.SessionsPerDevice), 1)]
    // SPEC §4.5: "received nothing for 60 seconds, it is dead".
    [InlineData(nameof(Limits.DeadSessionSeconds), 60)]
    // SPEC §4.6: "a small number such as 4".
    [InlineData(nameof(Limits.AdmissionPerAddress), 4)]
    // SPEC §7.1: "services: list<string(64)>(64)".
    [InlineData(nameof(Limits.MaxHelloServices), 64)]
    // SPEC §7.1, §7.10: "string(64)".
    [InlineData(nameof(Limits.MaxServiceNameLength), 64)]
    // SPEC §7.2: "packages: list<PackageState>(64)".
    [InlineData(nameof(Limits.MaxPackagesPerPage), 64)]
    // SPEC §7.2: "at most 65 536 PackageStates in all".
    [InlineData(nameof(Limits.MaxListingPackages), 65_536)]
    // SPEC §7.2: "MUST complete within 60 seconds of page 0".
    [InlineData(nameof(Limits.ListingSeconds), 60)]
    // SPEC §7.2: "grants: list<hash>(256)".
    [InlineData(nameof(Limits.MaxPackageGrants), 256)]
    // SPEC §7.2: "heads: list<Head>(256)".
    [InlineData(nameof(Limits.MaxPackageHeads), 256)]
    // SPEC §7.4: "hashes: list<hash>(1024)".
    [InlineData(nameof(Limits.MaxHaveHashes), 1024)]
    // SPEC §7.8: "hashes: list<hash>(1024)".
    [InlineData(nameof(Limits.MaxCancelHashes), 1024)]
    // SPEC §7.4: "bound the sources it records to 65 536 hashes per peer".
    [InlineData(nameof(Limits.MaxRecordedSources), 65_536)]
    // SPEC §7.5: "hashes: list<hash>(64)".
    [InlineData(nameof(Limits.MaxWantHashes), 64)]
    // SPEC §7.5: "MUST NOT have more than 64 hashes outstanding".
    [InlineData(nameof(Limits.MaxOutstandingWants), 64)]
    // SPEC §7.6: "MUST NOT have more than 8 MiB of Object payload queued".
    [InlineData(nameof(Limits.MaxQueuedObjectBytes), 8 * MiB)]
    // SPEC §7.13: "detail: string(256)".
    [InlineData(nameof(Limits.MaxByeDetailLength), 256)]
    // SPEC §8.2: "1 to 1 048 576 bytes".
    [InlineData(nameof(Limits.MinChunkLength), 1)]
    // SPEC §8.3: "chunks: biglist<hash>(32768)".
    [InlineData(nameof(Limits.MaxChunkListChunks), 32_768)]
    // SPEC §8.4: "entries: list<TreeEntry>(8192)".
    [InlineData(nameof(Limits.MaxTreeEntries), 8192)]
    // SPEC §8.4: "name: string(255)".
    [InlineData(nameof(Limits.MaxTreeNameLength), 255)]
    // PAYLOADS.md §2: "label: string(64)".
    [InlineData(nameof(Limits.MaxVersionLabelLength), 64)]
    // PAYLOADS.md §2: "prerelease: string(64)".
    [InlineData(nameof(Limits.MaxVersionPrereleaseLength), 64)]
    // PAYLOADS.md §2: "notes: string(4096)".
    [InlineData(nameof(Limits.MaxVersionNotesLength), 4096)]
    // PAYLOADS.md §3: "text: string(16384)".
    [InlineData(nameof(Limits.MaxMessageTextLength), 16_384)]
    // PAYLOADS.md §3: "attachments: list<Attachment>(64)".
    [InlineData(nameof(Limits.MaxMessageAttachments), 64)]
    // PAYLOADS.md §3: attachment "name: string(255)".
    [InlineData(nameof(Limits.MaxAttachmentNameLength), 255)]
    // SPEC §8.6: "parents: list<hash>(2)".
    [InlineData(nameof(Limits.MaxEntryParents), 2)]
    // SPEC §8.6: "references: list<hash>(64)".
    [InlineData(nameof(Limits.MaxEntryReferences), 64)]
    // SPEC §8.6: "payload: blob(1048576)".
    [InlineData(nameof(Limits.MaxPayloadLength), MiB)]
    // SPEC §8.7: "name: string(255)".
    [InlineData(nameof(Limits.MaxManifestNameLength), 255)]
    // SPEC §8.7: "branches: list<Branch>(64)".
    [InlineData(nameof(Limits.MaxManifestBranches), 64)]
    // SPEC §8.7: branch "name: string(64)".
    [InlineData(nameof(Limits.MaxBranchNameLength), 64)]
    // SPEC §8.8: "devices: list<RosterDevice>(256)".
    [InlineData(nameof(Limits.MaxRosterDevices), 256)]
    // SPEC §8.8: device "name: string(64)".
    [InlineData(nameof(Limits.MaxDeviceNameLength), 64)]
    // SPEC §10.6: "at most 8 MiB per session".
    [InlineData(nameof(Limits.MaxHeldBytesPerSession), 8 * MiB)]
    // SPEC §10.6: "and 64 MiB in total".
    [InlineData(nameof(Limits.MaxHeldBytes), 64 * MiB)]
    // SPEC §10.6: "unverifiable 10 minutes after its sender disconnected is dropped".
    [InlineData(nameof(Limits.HeldAfterDisconnectSeconds), 10 * 60)]
    // SPEC §10.3: "except when a peer has not answered within 30 seconds".
    [InlineData(nameof(Limits.ReaskSeconds), 30)]
    // LOCAL §12: "Hello after connecting | 10 s".
    [InlineData(nameof(Limits.LocalHelloSeconds), 10)]
    // LOCAL §12: "connections waiting for Hello | 16".
    [InlineData(nameof(Limits.MaxConnectionsWaitingForHello), 16)]
    // LOCAL §12: "Ping after 60 s without receiving".
    [InlineData(nameof(Limits.LocalPingAfterSeconds), 60)]
    // LOCAL §12: "Pong after Ping | 30 s".
    [InlineData(nameof(Limits.LocalPongSeconds), 30)]
    // LOCAL §12: "outstanding requests per connection | 64".
    [InlineData(nameof(Limits.MaxOutstandingRequests), 64)]
    // LOCAL §12: "outstanding callbacks per connection | 64".
    [InlineData(nameof(Limits.MaxOutstandingCallbacks), 64)]
    // LOCAL §12: "subscriptions per connection | 64".
    [InlineData(nameof(Limits.MaxSubscriptions), 64)]
    // LOCAL §12: "queued events per subscription | 256 events".
    [InlineData(nameof(Limits.MaxQueuedEvents), 256)]
    // LOCAL §12: "or 16 MiB".
    [InlineData(nameof(Limits.MaxQueuedEventBytes), 16 * MiB)]
    // LOCAL §12: "replay per Subscribe | 256 Entries".
    [InlineData(nameof(Limits.MaxReplayEntries), 256)]
    // LOCAL §12: "64 MiB per connection".
    [InlineData(nameof(Limits.MaxUnacceptedBytesPerConnection), 64 * MiB)]
    // LOCAL §12: "256 MiB per Host".
    [InlineData(nameof(Limits.MaxUnacceptedBytesPerHost), 256 * MiB)]
    // LOCAL §12: "or 30 s for one frame".
    [InlineData(nameof(Limits.UnacceptedFrameSeconds), 30)]
    // LOCAL §12: "running operations per connection | 16".
    [InlineData(nameof(Limits.MaxRunningOperations), 16)]
    // LOCAL §12: "ended operations kept per connection | 48".
    [InlineData(nameof(Limits.MaxEndedOperations), 48)]
    // LOCAL §12: "user connections | 64".
    [InlineData(nameof(Limits.MaxUserConnections), 64)]
    // LOCAL §12: "service connections | 1 per configured name".
    [InlineData(nameof(Limits.ServiceConnectionsPerName), 1)]
    // LOCAL §12: "pairings per Host | 1 at a time".
    [InlineData(nameof(Limits.PairingsPerHost), 1)]
    // LOCAL §12: "10 minutes".
    [InlineData(nameof(Limits.PairingSeconds), 10 * 60)]
    // LOCAL §12: "static peers | 256".
    [InlineData(nameof(Limits.MaxStaticPeers), 256)]
    // LOCAL §12: "Drain grace | 5 000 ms default".
    [InlineData(nameof(Limits.DrainGraceDefaultMilliseconds), 5_000)]
    // LOCAL §12: "30 000 ms at most".
    [InlineData(nameof(Limits.DrainGraceMaxMilliseconds), 30_000)]
    // LOCAL §12: "ServiceCall / ForwardedCall | 60 s".
    [InlineData(nameof(Limits.ServiceCallSeconds), 60)]
    // LOCAL §12: "Fetch without a source | 60 s".
    [InlineData(nameof(Limits.FetchWithoutSourceSeconds), 60)]
    // LOCAL §12: "page of ListHistory, ListStored | 64 Entries".
    [InlineData(nameof(Limits.HistoryPageEntries), 64)]
    // LOCAL §12: "page of ListPackages, ListPins | 256".
    [InlineData(nameof(Limits.PackagePageEntries), 256)]
    // LOCAL §12: "points per QueryTelemetry | 10 080 (7 days)".
    [InlineData(nameof(Limits.MaxTelemetryPoints), 10_080)]
    // LOCAL §12: "telemetry retention | 7 days of minutes".
    [InlineData(nameof(Limits.TelemetryRetentionDays), 7)]
    // LOCAL §3.2: "A service name is at most 32 bytes".
    [InlineData(nameof(Limits.MaxLocalServiceNameLength), 32)]
    // LOCAL §4.2: a token of 32 bytes.
    [InlineData(nameof(Limits.ServiceTokenLength), 32)]
    public void EveryLimitHasTheValueItsSectionStates(string name, int expected)
    {
        FieldInfo? field = typeof(Limits).GetField(name, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(expected, Convert.ToInt32(field.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture));
    }
}
