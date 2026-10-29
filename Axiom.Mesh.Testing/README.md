# Axiom.Mesh.Testing

An in-memory [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh) of any number of nodes for tests: one fake clock per mesh, fake discovery with partitions, in-memory stores, scripted peers that speak raw AMP/1, and waits backed by the nodes' facts, never by sleeps.

Namespace: `Axiom.Mesh.Testing`. Depends on `Axiom.Mesh.Host` (and so on Core and everything below it).

## Status

Phase 1a, in progress. Built (milestone M6): the published test identities, `TestMesh` with trust seeding, connect,
stop and start, partition and heal, `FakeDiscovery`, `InMemoryDialer`, `InMemoryKeyStore`, `InMemoryNetwork` and
`ScriptedPeer`. Pairing (`PairAsync`, `EnrolAsync`), the in-process CLI and `SettleAsync` follow with the milestones that
build them.

## Quick Start

Two published devices whose users know each other connect through real mutual TLS 1.3 and AMP/1, in memory; Eve's
device, which nobody knows, is closed before a protocol byte.

```csharp
using Axiom.Mesh.Core;
using Axiom.Mesh.Testing;

await using TestMesh mesh = await TestMesh.StartAsync(
    TestMeshOptions.Of("alice-laptop", "bob-desktop") with { Trust = TrustSeed.MutuallyKnown("alice-laptop", "bob-desktop") },
    CancellationToken.None);

// Waits on the nodes' facts until both hold one established session to the other (15 s of real time at most).
await mesh.ConnectAsync("alice-laptop", "bob-desktop", CancellationToken.None);

PeerRecord bob = mesh["alice-laptop"].Node.Peers.Single(p => p.Device == mesh["bob-desktop"].Device);
Console.WriteLine(bob.State);                               // Established

// A peer driven by hand, with any published identity: the handshake refuses Eve's key without a byte of AMP/1.
await using ScriptedPeer eve = await ScriptedPeer.ConnectAsync(mesh["alice-laptop"], TestIdentity.Of("eve-device"), ScriptedPeerOptions.Default, CancellationToken.None);
await eve.ExpectClosedWithoutHelloAsync(CancellationToken.None);

// Time passes only when the test says so: a heartbeat, a backoff, a handshake deadline.
mesh.Clock.Advance(TimeSpan.FromSeconds(60));
```

## What is in it

| Type | What it is |
|---|---|
| `TestKeys` | The seven published PKCS#8 keys (users alice, bob, eve; devices alice-laptop, alice-phone, bob-desktop, eve-device), as in `Documentation/vectors/vectors.json`. Compiles against Model alone, so the lower suites link the file. |
| `TestIdentity` | A published device with its user: `Of(name)`, `All`, `RosterOf(user)` (the user's first Roster, always the same object), `RosterOf(user, seq, removed…)` (a new one, signed anew). |
| `TestMesh` | The mesh: `StartAsync(TestMeshOptions)`, `this[name]`, `Clock`, `Network`, `ConnectAsync`, `WaitUntilAsync`, `WaitForFactAsync`, `StopNodeAsync`, `StartNodeAsync`, `Partition`, `Heal`. |
| `TestMeshOptions`, `TrustSeed` | Which nodes, which of them know which users (`MutuallyKnown`, `And`), which keep silent (dial but are never dialled), the seed. |
| `TestNode` | One node: its identity, address (`10.0.0.n:41810`), what it keeps across restarts, its discovery, the running `MeshNode`. |
| `TestNodeEnvironment` | In-memory LDB storage, object files and key store that survive a restart, on the mesh's clock with seeded randomness. |
| `InMemoryNetwork` | Who listens where, the discovery board, the open links; `Partition` cuts the links across and `Heal` joins again. |
| `FakeDiscovery` | A node's discovery on the board (`Announce`, `Withdraw`, `IsSilent`); claims already on the board are heard first. |
| `InMemoryDialer` | Connects to the target node's server in memory, so both ends run admission, TLS and AMP/1 as over TCP. |
| `InMemoryKeyStore` | An `IKeyStore` in memory. |
| `ScriptedPeer` | A peer that connects (`ConnectAsync`) or is dialled (`AcceptAsync`) with any identity and speaks raw AMP/1: `SendAsync`, `SendRawAsync`, `ExpectAsync<T>`, `ExpectByeAsync`, `ExpectClosedAsync`, `ExpectClosedWithoutHelloAsync`. |
| `TestMeshException` | A wait that ran out, or an expectation the node did not meet, with the nodes' last facts. |

## Rules of use

- **Trust is seeded before discovery opens.** `StartAsync` starts every node with its own user's first Roster as its
  identity, applies the `TrustSeed`, and only then releases the discovery board, so every dial meets a node that already
  knows whom it trusts. Nodes then find and dial each other on their own; `ConnectAsync` waits until they have.
- **Wait on facts.** Every wait re-checks after each fact a node records and gives up after 15 seconds of real time with
  the facts of every node in the message. A test never sleeps (a convention test refuses `Thread.Sleep` and `Task.Delay`).
- **Move the clock in the steps you mean.** A dial a retry timer starts runs after `Advance` returns; advancing far past
  it in one move expires the handshake it started. Advance to the moment you wait for (a backoff's end, a heartbeat), then
  wait for its fact.
- **Signatures are not deterministic.** BCL ECDSA signs with a random nonce, so `RosterOf(user, seq, …)` makes a new
  object each call; hand the same instance to every node that should hold it. `RosterOf(user)` is cached.
- **A scripted peer always presents its certificate.** On Windows an in-process TLS client given none presents one an
  earlier client of the process used, so a certificate-less peer cannot be scripted faithfully; that case is proved at the
  node's certificate callback.
