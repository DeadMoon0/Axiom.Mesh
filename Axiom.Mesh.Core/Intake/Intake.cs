using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core;

/// <summary>
/// Where every object from a peer enters the node (SPEC.md §10.6): the bytes are copied out of the caller's frame, their
/// hash checked against the claimed one, the object parsed, and <see cref="IntakeRules"/> decides by its tag; what is
/// storable goes to the <see cref="StoreWriter"/>, what waits goes to the <see cref="HeldBuffer"/> and is decided again
/// after every Roster, Manifest or Grant stored. Nothing reaches the store without a <see cref="StorableObject"/>.
/// </summary>
/// <remarks>
/// Signed objects are copied into arrays of their own, because a stored Roster, Manifest or Grant lives on in the trust
/// snapshot; everything else is copied into a pooled buffer the writer gives back once the object is on disk.
/// </remarks>
internal sealed class Intake
{
    private readonly StoreWriter writer;
    private readonly IWantedSet wanted;
    private readonly ArrayPool<byte> pool = PooledCopy.CreatePool();

    /// <summary>Intake in front of a store writer.</summary>
    /// <param name="writer">The store writer.</param>
    /// <param name="wanted">What the node asked for.</param>
    public Intake(StoreWriter writer, IWantedSet wanted)
    {
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.wanted = wanted ?? throw new ArgumentNullException(nameof(wanted));
    }

    /// <summary>The held buffer.</summary>
    public HeldBuffer Held { get; } = new();

    /// <summary>
    /// Takes in one object. A Roster, Manifest or Grant is awaited until stored, and the held buffer re-examined, before
    /// this returns; anything else admitted is queued and its completion handed back.
    /// </summary>
    /// <param name="claimed">The hash the sender named.</param>
    /// <param name="bytes">The object's bytes; copied before anything else, so the caller may reuse its buffer at once.</param>
    /// <param name="from">Who sent it.</param>
    /// <param name="context">The Package it was wanted for, when known.</param>
    /// <param name="nowMs">The time, in milliseconds since the Unix epoch.</param>
    /// <param name="cancellationToken">Cancels waiting for room in the writer's queue.</param>
    /// <returns>What became of it.</returns>
    public async ValueTask<IntakeOutcome> ReceiveAsync(ObjectHash claimed, ReadOnlyMemory<byte> bytes, Origin from, PackageId? context, long nowMs, CancellationToken cancellationToken)
    {
        (ReadOnlyMemory<byte> owned, PooledCopy? lease) = Copy(bytes.Span);
        if (!HashedObject.TryVerify(owned, claimed, out HashedObject hashed))
        {
            lease?.Dispose();
            return new IntakeOutcome(IntakeVerdict.Violation, "The bytes are not the object the sender named: H(object) differs (SPEC.md §7.6).", null, default);
        }

        if (!Objects.TryParse(hashed, out ParsedObject? parsed, out ObjectFault fault))
        {
            lease?.Dispose();
            return new IntakeOutcome(IntakeVerdict.Faulty, fault.Detail, null, default);
        }

        IntakeDecision decision = IntakeRules.Decide(parsed, writer.Trust, wanted);
        switch (decision.Verdict)
        {
            case IntakeVerdict.Admitted:
                Task<AdmitResult> stored = await writer.EnqueueAsync(new AdmitCommand(decision.Storable!, from, context, lease), cancellationToken).ConfigureAwait(false);
                if (ChangesTrust(decision.Storable!))
                {
                    await ((Task)stored).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                    await ReexamineAsync(cancellationToken).ConfigureAwait(false);
                }

                return new IntakeOutcome(IntakeVerdict.Admitted, null, stored, default);
            case IntakeVerdict.Held:
                Held.Add(new HeldObject(parsed, from, nowMs, decision.Need, context));
                return new IntakeOutcome(IntakeVerdict.Held, null, null, decision.Need);
            default:
                lease?.Dispose();
                return new IntakeOutcome(decision.Verdict, decision.Detail, null, default);
        }
    }

    /// <summary>
    /// Decides again on every held object against the trust that holds now, stores what became storable, drops what
    /// became faulty, and repeats while that stored a Roster, Manifest or Grant. Called after intake stored one, and by
    /// whoever changed trust otherwise (a follow, a pairing).
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for room in the writer's queue.</param>
    /// <returns>A task that completes when nothing held can be decided any more.</returns>
    public async Task ReexamineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TrustSnapshot trust = writer.Trust;
            var decisions = new Dictionary<HeldObject, IntakeDecision>(ReferenceEqualityComparer.Instance);
            IReadOnlyList<HeldObject> ready = Held.Take(item =>
            {
                IntakeDecision decision = IntakeRules.Decide(item.Object, trust, wanted);
                decisions[item] = decision;
                return decision.Verdict != IntakeVerdict.Held;
            });
            if (ready.Count == 0)
                return;

            var trustStored = new List<Task>();
            foreach (HeldObject item in ready)
            {
                IntakeDecision decision = decisions[item];
                if (decision.Verdict != IntakeVerdict.Admitted)
                    continue; // faulty or dropped now; its sender's session has moved on

                Task<AdmitResult> stored = await writer.EnqueueAsync(new AdmitCommand(decision.Storable!, item.Origin, item.Context, null), cancellationToken).ConfigureAwait(false);
                if (ChangesTrust(decision.Storable!))
                    trustStored.Add(stored);
            }

            if (trustStored.Count == 0)
                return;
            await Task.WhenAll(trustStored).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <summary>A sender disconnected: what it sent is dropped 10 minutes later unless it reconnects (SPEC.md §10.6).</summary>
    /// <param name="origin">The sender.</param>
    /// <param name="nowMs">The time, in milliseconds since the Unix epoch.</param>
    public void OriginGone(Origin origin, long nowMs) => Held.OriginGone(origin, nowMs);

    /// <summary>Drops what waited 10 minutes after its sender left.</summary>
    /// <param name="nowMs">The time, in milliseconds since the Unix epoch.</param>
    /// <returns>What was dropped.</returns>
    public IReadOnlyList<HeldObject> Expire(long nowMs) => Held.Expire(nowMs);

    private static bool ChangesTrust(StorableObject storable) => storable is VerifiedRoster or VerifiedManifest or VerifiedGrant;

    private (ReadOnlyMemory<byte> Owned, PooledCopy? Lease) Copy(ReadOnlySpan<byte> bytes)
    {
        bool signed = !bytes.IsEmpty && (ObjectTag)bytes[0] is ObjectTag.Entry or ObjectTag.Manifest or ObjectTag.Roster or ObjectTag.Grant;
        if (signed)
            return (bytes.ToArray(), null);

        PooledCopy lease = PooledCopy.Of(bytes, pool);
        return (lease.Memory, lease);
    }
}
