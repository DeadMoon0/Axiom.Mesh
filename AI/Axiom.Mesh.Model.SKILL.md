<identity>
    <package>Axiom.Mesh.Model</package>
    <role>core-skill</role>
    <requires>none</requires>
</identity>

<objective>
    Teach an agent the honest core of Axiom.Mesh: ids, objects, Entries and signatures, the object typestate that decides what may be stored, the order and validity rules, the protocol limits and the exception family.
</objective>

<package_scope>
    Namespace Axiom.Mesh (one namespace, folders for orientation only).
    Primitives: Hash32 (four big-endian ulongs; Of, Read, Write, TryParseHex, ToHex, IsZero, unsigned byte order), ObjectHash, UserId, DeviceId, PackageId (Derive(owner, nonce) = H("axiom-mesh/package/1" ‖ owner ‖ nonce)), PackageNonce, Signature64 (low-s by construction: TryParse refuses r or s out of bounds, Normalize maps s to n − s), PublicKey (sealed class, 65-byte point proven on P-256; Verify(SigningDigest, Signature64); AsUserId/AsDeviceId), SigningDomain (Entry, Manifest, Roster, Grant), SigningDigest, Digests.Of(domain, bytes), ISigner (the only seam for a private key), EcdsaSigner (FromPkcs8; low-s; thread-safe).
    Objects (the typestate): HashedObject.TryVerify(bytes, claimed) or .Of(own bytes) → Objects.TryParse → Chunk | ChunkList | Tree | OpaqueObject (storable as parsed) | UnverifiedEntry | UnverifiedManifest | UnverifiedRoster | UnverifiedGrant, else ObjectFault {Malformed|Faulty, Detail}. VerifiedManifest.TryVerify(m) (own owner key), VerifiedRoster.TryVerify(r) (own user key), VerifiedGrant.TryVerify(g, current VerifiedManifest), VerifiedEntry.TryVerify(e, device key) → EntryAdmission.Decide(VerifiedEntry, ITrustSnapshotView) → AdmissionDecision.Admit(AdmittedEntry) | Unverifiable(Prerequisite) | Invalid(AdmissionRefusal). Every stage exposes Hash, Bytes and Content (EntryContent, ManifestContent, RosterContent, GrantContent: what the bytes say, no trust).
    Entry envelope (SPEC draft 5): EntryContent { Kind, References (EntryReferences: at most 64, strictly ascending, Of(any order) sorts and dedups, TryFromAscending for stated lists, zero-copy on parse), Redacts (zero = none; IsRedaction), Payload (opaque bytes), Facts, Body }; EntryBody(kind, payload, references, redacts); Objects.TryParse never parses a payload; Redactions.Honours reads EntryFacts.Redacts.
    Payloads (namespace Axiom.Mesh.Payloads, for services and the CLI, never Core or Host): EntryPayload (VersionPayload, MessagePayload, RedactionPayload, OpaquePayload) with Kind, References, Redacts and ToBody(); EntryPayload.TryParse(kind, bytes); PayloadEncoder.Encode; layouts in PAYLOADS.md.
    Producers: EntryDraft { Package, Clock, Parents, Body }, ManifestDraft/RosterDraft/GrantDraft; each .Sign(ISigner …) encode, sign, re-parse and re-verify; ObjectBuilders.Chunk/ChunkList/TryTree; Chunker.Cut (1 MiB, in memory).
    Rules: EntryKey (total order), Head, Lamport (IsWellFormed, TryFindParentFault), ParentRule.Choose(ownHead, highest) → ParentChoice.Write(clock, parents) | ClockExhausted, HeadRule, CurrentObject (Supersedes, ChangesKind), RosterRules.Apply (permanent removal, omitted = removed, device of two users removed in the later), Visibility (MayList, MayClaimGrant, MayServeThrough, MayServeRoster over PackageAudience), Redactions.Honours, DuplicateSessions.Decide → KeptSession, ChunkListRule.
    Wire: the §5.2 string/blob/list types over ProtoStream's WireReader/WireWriter. Limits: every bound of SPEC.md and LOCAL-SPEC.md §12.
</package_scope>

<architecture>
    Pure functions of bytes, keys and values: no socket, disk, clock, randomness, LWSE or LDB. Depends on Axiom.ProtoStream (WireReader/WireWriter) only. StorableObject and ParsedObject have private-protected constructors and every subclass is sealed with an internal constructor, so outside Model a storable object of a signed kind exists only after TryVerify (and, for an Entry, EntryAdmission.Decide). The store and the Object message accept StorableObject only. Unsigned objects need only their hash.
</architecture>

<best_practices>
    Hash with HashedObject.TryVerify against the claimed hash before parsing anything a peer sent. Look up the writer's key with ITrustSnapshotView.TryGetDevice, then VerifiedEntry.TryVerify, then EntryAdmission.Decide; hold an Unverifiable Entry and decide again when its Prerequisite arrives. Use ParentRule.Choose for the clock and parents of a new Entry and stop on ClockExhausted. Apply every new current Roster with RosterRules.Apply and keep its RemovedEver. Compare ids and hashes with their operators (unsigned byte order). Throw MeshException only for caller, configuration or environment faults; a peer's mistake is an ObjectFault or an AdmissionDecision; argument mistakes throw ArgumentException before anything happened.
</best_practices>

<pitfalls>
    Never re-encode before hashing or verifying: signatures cover the received bytes before the signature, tag and version included (SPEC.md 2.2). A HashedObject and everything parsed from it refer to the caller's memory; copy a reusable frame buffer before parsing, or keep the frame alive. BCL ECDSA is not deterministic, so signed vectors are verified, never byte-compared (STRUCTURAL-DEBT.md entry 1). Unknown tags, Entry kinds and Package kinds are carried, not refused: an enum may hold a value it does not name. Regenerate Documentation/vectors only with AXIOM_MESH_WRITE_VECTORS=1, and commit the result.
</pitfalls>
