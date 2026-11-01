# Axiom.Mesh.Testing

An in-memory [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh) of any number of nodes for tests: one fake clock per mesh, fake discovery with partitions, in-memory stores, scripted peers that speak raw AMP/1, and waits backed by the nodes' facts, never by sleeps.

Namespace: `Axiom.Mesh.Testing`. Depends on `Axiom.Mesh.Core` (and so on everything below it), LDB's in-memory storage
and `Microsoft.Extensions.TimeProvider.Testing`. The command line (`Axiom.Mesh.Cli.dll`), which `TestNode.CliAsync` runs
in-process, travels inside this package: its own package is a dotnet tool, which no package can depend on. The daemon
(`Axiom.Mesh.Host`) is not needed: a test node is a `MeshNode` with in-memory seams.

## Status

0.1.0. `TestMesh` (trust seeding, connect, stop and start, partition, severed links, heal, `SettleAsync`, `PairAsync`,
`EnrolAsync`, `WaitForPairingAsync`), `TestNode` (content seams: create, grant, follow, write, publish, wait for an
Entry or object; local clients: `ConnectUserAsync`, `ConnectServiceAsync`, `ConnectInProcessAsync`, `ConnectAsync`,
`ConnectRaw` from a `TestProcess`; the command line in-process: `CliAsync`), the published test identities, fake
discovery, the in-memory dialler, key store and network, `ScriptedPeer` (raw AMP/1 and pairing frames over the node's
own mutual TLS, or over any open connection), and `IMeshFixture`, which `TestMesh` implements in memory and the
integration suite's socket fixture on real nodes, so one scenario source runs both ways.

Since 0.2.0 (unreleased) a service runs in process beside a node: `TestServices.With(TestCliService.Local, definition)`
lists it on the mesh's nodes as an external service with a published token (`TestServices.TokenOf`), and
`TestNode.RunServiceAsync(service)` (or `IMeshFixture.RunServiceAsync(node, service)`) runs the runtime of
`Axiom.Mesh.Service` over an in-memory pair to the node's local port, its state in an in-memory LDB the node keeps across
runs, on the mesh's clock; it returns a `RunningService` once the service is connected. `TestEchoService` is the
smallest service (it counts the Entries of its kind, answers `echo`, `count` and `write`, emits `seen`).
`RunServiceAsync(service, stateOf: other, ct)` runs a service on another node's kept state, which a test of another
store epoch needs. The command line in-process (`CliAsync`) includes the `chat` commands.

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

Two nodes replicate one Entry: Alice creates a Channel and grants Bob read, Bob follows it, and what Alice writes reaches
Bob's store by itself, pushed, with no clock moved.

```csharp
using Axiom.Mesh;

await using TestMesh pair = await TestMesh.StartAsync(
    TestMeshOptions.Of("alice-laptop", "bob-desktop") with { Trust = TrustSeed.MutuallyKnown("alice-laptop", "bob-desktop") },
    CancellationToken.None);
TestNode alice = pair["alice-laptop"];
TestNode bob = pair["bob-desktop"];

PackageId channel = await alice.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, CancellationToken.None);
await alice.GrantAsync(channel, "bob", GrantRights.Read, CancellationToken.None);
await bob.FollowAsync(channel, CancellationToken.None);
await pair.ConnectAsync("alice-laptop", "bob-desktop", CancellationToken.None);

ObjectHash hello = await alice.WriteEntryAsync(channel, ObjectFixtures.Message("hello, Bob"), CancellationToken.None);
await bob.WaitForEntryAsync(hello, CancellationToken.None);   // on Bob's ObjectStored facts
await pair.SettleAsync(CancellationToken.None);               // nothing left in flight on either node
Console.WriteLine(bob.Node.Replication);                      // wants, counters, the session's traffic
```

Two users who do not know each other pair over their local ports, as two people at their screens would, and then
establish; the command line runs in-process against a node, as it runs against a Host.

```csharp
await using TestMesh strangers = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop", "bob-desktop"), CancellationToken.None);

string sas = await strangers.PairAsync("alice-laptop", "bob-desktop", CancellationToken.None);   // show, join, compare, confirm
Console.WriteLine(sas);                                                                         // such as K7QD-M2XA
await strangers.ConnectAsync("alice-laptop", "bob-desktop", CancellationToken.None);

CliResult users = await strangers["alice-laptop"].CliAsync("users --json", CancellationToken.None);
Console.WriteLine(users.ExitCode);                                                              // Ok
```

A scenario written once against `IMeshFixture` runs on any mesh that implements it: in memory on `TestMesh`, or on real
nodes with sockets, disks and the real pipe (the repository's integration suite runs its scenario suite both ways, from
one source file). Nodes start as devices of their own users; users meet by pairing; the command line writes as the
`cli` service of `TestCliService`.

```csharp
using Axiom.Mesh.Local;

async Task<string> PairWriteAndFollowAsync(IMeshFixture fixture, CancellationToken ct)
{
    await fixture.PairAsync("alice-laptop", "bob-desktop", ct);
    await fixture.ConnectAsync("alice-laptop", "bob-desktop", ct);
    await fixture.HoldUserKeyAsync("alice-laptop", ct);
    await using LocalClient owner = await fixture.ConnectUserAsync("alice-laptop", "readme", ct);
    Package family = (await owner.SendAsync(new CreatePackage { Kind = PackageKind.Channel, Name = "Family" }, ct)).Value;
    await owner.SendAsync(new SetGrant { PackageId = family.PackageId, GranteeId = TestIdentity.Of("bob-desktop").UserId, Rights = GrantRights.Read }, ct);
    await fixture.CliAsync("alice-laptop", $"write {family.PackageId.Value.ToHex()} --text 'hello, Bob'", ct);
    await fixture.SettleAsync(ct);                                // Bob's node now lists the Channel

    await using LocalClient follower = await fixture.ConnectUserAsync("bob-desktop", "readme", ct);
    await follower.SendAsync(new FollowPackage { PackageId = family.PackageId }, ct);
    await fixture.SettleAsync(ct);                                // and holds what Alice wrote
    return (await fixture.CliAsync("bob-desktop", $"history {family.PackageId.Value.ToHex()}", ct)).Stdout;
}

await using IMeshFixture inMemory = await TestMesh.StartAsync(
    TestMeshOptions.Of("alice-laptop", "bob-desktop") with { Local = TestCliService.Local },
    CancellationToken.None);
Console.WriteLine(await PairWriteAndFollowAsync(inMemory, CancellationToken.None));   // the Entry, on Bob's node
```

## What is in it

| Type | What it is |
|---|---|
| `TestKeys` | The seven published PKCS#8 keys (users alice, bob, eve; devices alice-laptop, alice-phone, bob-desktop, eve-device), as in `Documentation/vectors/vectors.json`. Compiles against Model alone, so the lower suites link the file. |
| `TestIdentity` | A published device with its user: `Of(name)`, `All`, `RosterOf(user)` (the user's first Roster, always the same object), `RosterOf(user, seq, removed…)` (a new one, signed anew). |
| `TestMesh` | The mesh: `StartAsync(TestMeshOptions)`, `this[name]`, `Clock`, `Network`, `ConnectAsync`, `WaitUntilAsync`, `WaitForFactAsync`, `SettleAsync`, `StopNodeAsync`, `StartNodeAsync`, `Partition`, `Sever`, `Heal`, `CreateIdentityAsync(node, user, device)` (over the local port, for a node without identity), `PairAsync(showing, entering)` (ShowUser and JoinUser, both confirm; returns the SAS), `EnrolAsync(user, device)` (AddDevice and ShowDevice; the user key placed first, the device's identity awaited), `WaitForPairingAsync(node, client, condition, what)`. |
| `TestMeshOptions`, `TrustSeed` | Which nodes, which of them know which users (`MutuallyKnown`, `And`), which keep silent (dial but are never dialled), which pairs are severed from the start, the seed, which start without an identity (`WithoutIdentity`), what every local port is told (`Local`), where nodes log (`Loggers`), which keep their store in a real directory (`StoreDirectories`, layout 1 of LOCAL-SPEC.md §9.7). |
| `TestNode` | One node: its identity, address (`10.0.0.n:41810`), what it keeps across restarts, its discovery, the running `MeshNode`; and content seams: `CreatePackageAsync`, `GrantAsync`, `FollowAsync`, `WriteEntryAsync` (a payload of PAYLOADS.md, or any `EntryBody`), `PublishAsync`, `WaitForEntryAsync`, `WaitForObjectAsync`, `HoldUserKeyAsync` (the user key into the node's key store, for the user-key requests over the local port); and local clients: `ConnectUserAsync(name)`, `ConnectServiceAsync(name, token)`, `ConnectInProcessAsync(name)`, `ConnectAsync(options, process)` (each a `LocalClient` the mesh closes when it stops the node) and `ConnectRaw(process)` (the client's end of a connection, for frame-level tests); `CliAsync(commandLine)` runs `axiom-mesh` in-process against the node, as the served user and, for the write commands, as service `cli` with the token `TestMeshOptions.Local` configures (`CliResult`: exit code, stdout, stderr; an overload echoes stdout as it is written, for `watch`). |
| `TestProcess` | The process a local connection pretends to come from: `Served` (pid 4242, user `tester`, the user every test node serves), `AsOtherUser()`, `Running(executable, pid)`. |
| `ObjectFixtures`, `TreeFixture` | Deterministic content (`Content`), a folder cut into Chunks with its ChunkLists and Tree (`Folder`), a Package filled with Messages (`WriteMessagesAsync`), `Message` and `Version` payloads. |
| `TestNodeEnvironment` | In-memory LDB storage, object files (or a directory) and key store that survive a restart, on the mesh's clock with seeded randomness. |
| `InMemoryNetwork` | Who listens where, the discovery board, the open links; `Partition` cuts the links across, `Sever` cuts one pair, `Heal` joins again. |
| `FakeDiscovery` | A node's discovery on the board (`Announce`, `Withdraw`, `IsSilent`); claims already on the board are heard first. |
| `InMemoryDialer` | Connects to the target node's server in memory, so both ends run admission, TLS and AMP/1 as over TCP. |
| `InMemoryKeyStore` | An `IKeyStore` in memory. |
| `ScriptedPeer` | A peer that connects (`ConnectAsync`) or is dialled (`AcceptAsync`) with any identity and speaks raw AMP/1: `SendAsync`, `SendRawAsync`, `SendFrameStartAsync` (a frame begun and never finished), `ExpectAsync<T>`, `ExpectByeAsync`, `ExpectClosedAsync`, `ExpectClosedWithoutHelloAsync`; on a connection under the pairing ALPN `SendPairAsync`, `ExpectPairAsync<T>` and `ExpectPairingClosedAsync` (a frame of any other protocol fails the expectation). |
| `CliResult`, `CliWords` | What an in-process command line left; a command line split into arguments as a shell would. |
| `IMeshFixture` | A running mesh of published devices as a scenario sees it, in memory or on sockets: `Clock`, `Node(name)`, `PairAsync`, `EnrolAsync`, `HoldUserKeyAsync`, `ConnectAsync`, `SettleAsync`, `WaitUntilAsync`, `ConnectUserAsync`, `ConnectCliServiceAsync`, `CliAsync`, `ConnectScriptedPeerAsync`, `StopNodeAsync`, `StartNodeAsync`. `TestMesh` implements it. |
| `TestCliService` | The external service `cli` with a published token (`Token`, `Service`, `Local`), as a Host's default `config.json` lists it. |
| `MeshWaits`, `MeshPairing` | The conditions every fixture's waits re-check (`IsEstablishedWith`, `IsSettled`), and one pairing driven over two local ports (`RunAsync`, `WaitForAsync`), shared by the in-memory and the socket fixture. |
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
- **Settle before you count.** `SettleAsync` returns once every running node is idle (no store command, change set, want
  or queued message left), every node holds one session per peer, and every message one side sent the other side has
  handled; a want no node can answer does not hold it up. Read counters and message totals only after it.
- **Connect before you count, and know who dialled.** `ConnectAsync` returns only once both nodes hold one session each and
  no second connection between them is still in its handshake, so advancing the clock afterwards expires nothing. Start
  nodes `Silent` when a test must set something up (a Roster, a subscription) before the first `Hello`.
- **Signatures are not deterministic.** BCL ECDSA signs with a random nonce, so `RosterOf(user, seq, …)` makes a new
  object each call; hand the same instance to every node that should hold it. `RosterOf(user)` is cached.
- **A scripted peer always presents its certificate.** On Windows an in-process TLS client given none presents one an
  earlier client of the process used, so a certificate-less peer cannot be scripted faithfully; that case is proved at the
  node's certificate callback.
