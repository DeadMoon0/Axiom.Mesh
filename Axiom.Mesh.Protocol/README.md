# Axiom.Mesh.Protocol

AMP/1, the mesh wire protocol of [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh) (SPEC.md), described with
ProtoStream: the sixteen messages, the frame codec, the session states and limits, the pure rules a session checks
(`Hello`, listings, wants, queued objects) and the pairing protocol `axiom-mesh-pair/1` (PAIR-SPEC.md). Definitions,
codecs and pure functions only: no socket, no clock, no randomness.

Namespace: `Axiom.Mesh.Protocol`. Depends on `Axiom.Mesh.Model` and `Axiom.ProtoStream`.

**A building block of Core and Local, not referenced directly.** It is published on nuget.org as Core's dependency (one
version for every Axiom.Mesh package); embed a node with `Axiom.Mesh.Core`, or test against one with
`Axiom.Mesh.Testing`, whose scripted peers speak AMP/1 through this package.

## Quick Start

```csharp
using Axiom.Mesh;
using Axiom.Mesh.Protocol;
using Axiom.ProtoStream;

// One AMP/1 session over the stream the TLS layer handed over. The engine owns the connection, the trust snapshot and
// the clock; this package says what is allowed and what it means.
static async Task ServeAsync(Stream tls, Hello own, PeerIdentityFacts peer, ITrustSnapshotView trust, TimeProvider time, CancellationToken ct)
{
    await using Connection connection = Connection.FromStream(tls, new ConnectionOptions { TimeProvider = time });
    Session<MeshMessage, MeshMessage> session = await connection.OpenAsync(Amp.Definition, ct);

    // Each side sends Hello first (SPEC.md §6.1); the peer's moves the session to Established and is checked at once.
    await session.WriteAsync(own, ct);
    if ((await session.ReadAsync(ct)).Message is not Hello hello)
        return;
    if (HelloRule.Check(hello, peer, trust) is HelloOutcome.Refused refused)
    {
        await session.WriteAsync(refused.Bye, ct);
        return;
    }

    ListingRule listing = ListingRule.Idle;
    WantBudget wants = WantBudget.Empty;
    await foreach (MeshMessage message in session.Messages.WithCancellation(ct))
    {
        Bye? violation = null;
        switch (message)
        {
            case Packages page:
                ListingStep step = listing.OnPackages(page, time.GetUtcNow());
                listing = step.Next;
                violation = step.Bye;
                break;
            case Want want:
                if (wants.TryWant(want.Hashes.Count, out WantBudget next))
                    wants = next;
                else
                    violation = new Bye(ByeReason.Violation, "more than 64 wants outstanding");
                break;
            case ObjectMessage received:
                // Its hash was checked when the frame was decoded; it is a view of the frame, valid until the next read.
                if (Objects.TryParse(received.Object, out ParsedObject? parsed, out ObjectFault fault))
                    Console.WriteLine($"received {parsed.Tag} {parsed.Hash}");
                else
                    Console.WriteLine($"faulty content from the peer: {fault.Detail}");
                break;
        }

        if (violation is not null)
        {
            await session.WriteAsync(violation, ct);
            return;
        }
    }
}
```

Pairing is the same pattern on `PairProtocol.Definition`: the entering side parses the code it was given, and both
sides drive one `PairingMachine` with the session's events, the user's decisions and timestamps:

```csharp
using Axiom.Mesh.Protocol;

const string text = "amp1:user:192.168.1.20:41810:BAHQZP-5s6xgJVE9hQ4FtJpCkTC93gmPiMjGoweUonNDT0k_LgO8ucsM195yaEnZwKbeXkgxP5FHI-6Nr0Csa8k";
if (PairingCode.TryParse(text, out PairingCode? code)
    && PairingMachine.TryStart(current: null, code.EnteringMode, DateTimeOffset.UtcNow, out PairingMachine? pairing))
{
    // Dial code.Addresses under PairProtocol.Alpn and accept only code.DeviceKey; then, once PairingRules.TryAcceptHello
    // accepted the other side's PairHello, show Sas.Derive(...) and wait for the user.
    Console.WriteLine($"{pairing.Mode} {pairing.State}, expires {pairing.Expires:t}; dialling {code.Addresses[0]}");
}
```

## What is in it

| Folder | Types |
|---|---|
| root | `Amp` (ALPN, version, states, `Definition`) |
| `Messages/` | `MeshMessage` and its sixteen messages (`Hello`, `Packages` with `PackageState`, `PackageChanged`, `Have`, `Want`, `ObjectMessage`, `NotFound`, `Cancel`, `RosterQuery`, `RosterNotFound`, `ServiceOpen`/`Accepted`/`Refused`, `Ping`, `Pong`, `Bye`), `Extension`, `MessageIds`, `ByeReason`, `Capabilities`, `ServiceRefusal` |
| `Rules/` | `HelloRule` → `HelloOutcome`, `PeerIdentityFacts`, `RosterFollowUp`; `ListingRule` → `ListingStep`; `WantBudget`; `ObjectQueueRule` |
| `Pairing/` | `PairProtocol`, `PairHello`, `PairConfirm`, `PairCancel`, `PairingCode`, `PairingAddress`, `PairingTarget`, `Sas`, `PairingRules` → `PairPeer`, `PairingMachine` → `PairingStep`, `PairingMode`, `PairingState` |

## Rules of use

- **Hello first, checked at once.** Write the own `Hello` before the first read; the definition moves to Established on
  the peer's `Hello`, and the handler checks it with `HelloRule.Check` before it reads or sends anything else.
- **An `ObjectMessage` received is a view of its frame.** Its hash is verified on decode, nothing is copied, and every
  member throws `StaleMessageException` after the next read: parse it (the parsed object refers to the same memory) or
  copy it first. To send, `ObjectMessage.From(StorableObject)`: nothing unverified is ever forwarded.
- **Violations close, faults do not.** A frame the codec refuses, a message in the wrong state, a failed `Hello`, a
  broken listing or a 65th want ends the session with `Bye`; a malformed object inside a valid `Object` is faulty content
  (SPEC.md §12.2), handled by the caller.
- **Rules take timestamps.** `ListingRule`, `PairingMachine` and the rest read no clock; feed them the session's
  `TimeProvider` and arm timers for `ListingRule.Deadline` and `PairingMachine.Expires`.
- **Not enforced by the definition:** the 30 seconds within which a frame must complete after its first byte (SPEC.md
  §5.1). ProtoStream bounds a frame by the idle timeout (5 minutes); the session handler must add the deadline.
