# Axiom.Mesh.Model

The honest core of [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh): ids, objects, Entries and their signatures,
the history's total order and the rules that decide what is valid (SPEC.md §2, §5, §8, §9). Pure functions over bytes
and keys: no socket, no disk, no clock, no randomness.

Namespace: `Axiom.Mesh`. Depends on `Axiom.ProtoStream` (`WireReader`/`WireWriter`) only.

**A building block of Core and Local, not referenced directly.** It is published on nuget.org as their dependency (one
version for every Axiom.Mesh package); a program that talks to the Host references `Axiom.Mesh.Local`, one that embeds a
node `Axiom.Mesh.Core`, and gets this package with them. A service that builds or parses Entry payloads uses its
`Axiom.Mesh.Payloads` namespace through them.

## Quick Start

```csharp
using System.Security.Cryptography;
using Axiom.Mesh;
using Axiom.Mesh.Payloads;

// Keys live in the Host's key store; Model only ever sees an ISigner.
using ECDsa userKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
using ECDsa laptopKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
using EcdsaSigner user = EcdsaSigner.FromPkcs8(userKey.ExportPkcs8PrivateKey());
using EcdsaSigner laptop = EcdsaSigner.FromPkcs8(laptopKey.ExportPkcs8PrivateKey());

// The user's Roster lists the laptop; the user creates a Channel. Producers sign, re-parse and re-verify.
VerifiedRoster roster = new RosterDraft { Seq = 1, Devices = [new RosterDevice(laptop.PublicKey, "laptop", 0, removed: false)] }.Sign(user);
VerifiedManifest channel = new ManifestDraft
{
    Nonce = new PackageNonce(new Hash32(RandomNumberGenerator.GetBytes(32))),
    Kind = PackageKind.Channel,
    Seq = 1,
    Name = "Family",
    Created = 0,
}.Sign(user);

// The laptop writes the first message; the parent rule chooses its clock and parents. The payload is the service's
// (PAYLOADS.md); ToBody fills the envelope a node acts on: the references and the Entry it redacts.
var next = (ParentChoice.Write)ParentRule.Choose(ownHead: null, highest: null);
VerifiedEntry written = new EntryDraft
{
    Package = channel.Content.Package,
    Clock = next.Clock,
    Parents = next.Parents,
    Body = new MessagePayload { Text = "Hello", Attachments = [], ReplyTo = default }.ToBody(),
}.Sign(laptop);

// A receiving node: hash, parse, verify, admit, in that order. Only a StorableObject may be stored or forwarded.
var trust = new RosterTrust(channel, roster);
if (HashedObject.TryVerify(written.Bytes, written.Hash, out HashedObject hashed)
    && Objects.TryParse(hashed, out ParsedObject? parsed, out _)
    && parsed is UnverifiedEntry entry
    && trust.TryGetDevice(entry.Content.Device, out TrustedDevice device)
    && VerifiedEntry.TryVerify(entry, device.Key, out VerifiedEntry? verified)
    && EntryAdmission.Decide(verified, trust) is AdmissionDecision.Admit admit)
{
    Console.WriteLine($"store {admit.Entry.Hash} at {admit.Entry.Content.Key}");
}
```

The node's trust is whatever implements `ITrustSnapshotView`; the engine keeps an immutable snapshot, this one holds a
single Package and Roster:

```csharp
using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh;

public sealed class RosterTrust(VerifiedManifest manifest, VerifiedRoster roster) : ITrustSnapshotView
{
    public bool TryGetManifest(PackageId package, [NotNullWhen(true)] out VerifiedManifest? found)
    {
        found = package == manifest.Content.Package ? manifest : null;
        return found is not null;
    }

    public bool TryGetGrant(PackageId package, UserId grantee, [NotNullWhen(true)] out VerifiedGrant? grant)
    {
        grant = null;
        return false;
    }

    public bool TryGetDevice(DeviceId id, out TrustedDevice device)
    {
        foreach (EffectiveDevice listed in RosterRules.Apply(null, roster, new HashSet<DeviceId>(), new HashSet<DeviceId>()).Devices)
        {
            if (listed.Device.Id == id)
            {
                device = new TrustedDevice(id, roster.Content.User, listed.Device.Key, listed.IsRemoved);
                return true;
            }
        }

        device = default;
        return false;
    }
}
```

## What is in it

| Folder | Types |
|---|---|
| `Primitives/` | `Hash32`, `ObjectHash`, `UserId`, `DeviceId`, `PackageId` (`Derive`), `PackageNonce`, `Signature64` (low-s), `PublicKey` (on-curve), `SigningDomain`, `SigningDigest`, `Digests`, `ISigner`, `EcdsaSigner` |
| `Objects/` | the typestate `HashedObject` → `Objects.TryParse` → `ParsedObject` (`Chunk`, `ChunkList`, `Tree`, `OpaqueObject`, `Unverified{Entry,Manifest,Roster,Grant}`) or `ObjectFault` → `Verified*` → `StorableObject`; their contents and field types, the Entry envelope (`EntryBody`, `EntryReferences`, `EntryKind`) |
| `Payloads/` | namespace `Axiom.Mesh.Payloads`, for services and the command line, never for a node: the layouts of PAYLOADS.md, `EntryPayload` (`VersionPayload`, `MessagePayload`, `RedactionPayload`, `OpaquePayload`) with `TryParse` and `ToBody`, `PayloadEncoder` |
| `Producers/` | `EntryDraft`, `ManifestDraft`, `RosterDraft`, `GrantDraft`, `ObjectBuilders`, `Chunker` |
| `Rules/` | `EntryKey`, `Head`, `Lamport`, `ParentRule`, `HeadRule`, `CurrentObject`, `RosterRules`, `EntryAdmission`, `Visibility`, `Redactions`, `DuplicateSessions`, `ChunkListRule` |
| root | `Wire` (the §5.2 types over ProtoStream), `Limits` (every bound), the exception family |

## Rules of use

- **Peers' mistakes are values.** A malformed or faulty object is an `ObjectFault`, an invalid Entry an
  `AdmissionDecision.Invalid`; only a caller's mistake throws (an `ArgumentException` before anything happened).
- **Nothing is copied.** A `HashedObject` and everything parsed from it refer to the caller's memory; the bytes must not
  change while they are in use.
- **Verify the bytes received.** Signatures cover `H(domain ‖ bytes before the signature)` exactly as received.
- **Signatures are not deterministic.** The BCL's ECDSA draws a fresh nonce; both signatures verify
  (STRUCTURAL-DEBT.md entry 1). The published test vectors in `Documentation/vectors/` are verified, never byte-compared.
