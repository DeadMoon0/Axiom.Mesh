# Axiom.Mesh.Service

Build a service on [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh): the runtime that connects a service to its
Host, registers its Package kinds, catches it up on every Entry of them by store sequence, keeps it connected across
restarts of either side, hands it the calls apps make, and keeps its state in an LDB database of its own beside a cursor
that never skips an Entry. A service is the driver behind an app; it owns no user interface, and its namespace (methods
over `ServiceCall`, events over `Emit`) is its only surface.

Namespace: `Axiom.Mesh.Service`. Depends on `Axiom.Mesh.Local` (the local protocol and its client), `Axiom.Mesh.Model`,
LDB (`Axiom.LDB.Core`, `Axiom.LDB.Entity`, `Axiom.LDB.Query`) and `Microsoft.Extensions.Logging.Abstractions`; never on
the engine (`Axiom.Mesh.Core`) or the daemon. Inside an `Axiom.Mesh.*` namespace the namespace `Axiom.Mesh.Service`
hides the `Service` record of `Axiom.Mesh.Local` (what `ListServices` answers): write `Local.Service` there.

## Status

0.2.0, new. `IMeshService` and `ServiceDefinition`, `ServiceSession`, `ServiceState` with its `IndexCursor`,
`ServiceProgram` (the command line of a service executable: `run`, `version`), `ServiceShell` (the real environment and
the stop signal), `ServiceEnvironment`, `ServicePaths` and `ServiceConfig` (the Host's rules), the pure `CatchUpStep` and
`ReconnectRule`, `ServiceExitCode` and `ServiceConfigurationException`. The Host spawns services and supervises them
since 0.2.0 (LOCAL-SPEC.md draft 5, §4.2); the first two, Chat and Sync, follow.

## Quick Start

A service that counts the Entries of its kind and tells apps the count. `Program.cs` is one line; the runtime does the
rest: the token and the pipe from the environment the Host spawned it with (or from the Host's `config.json` when you run
it by hand as an external service), the state under its state directory, catch-up, reconnects and calls.

```csharp
using System.Buffers.Binary;
using Axiom.Mesh;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service;

return ServiceProgram.Run(new CountingService(), args);

public sealed class CountingService : IMeshService
{
    private long count;

    public ServiceDefinition Definition { get; } = new()
    {
        Name = "counter",
        Version = "1.0.0",
        Kinds = [new ServiceKind { Kind = (PackageKind)100, EntryKinds = [(EntryKind)100] }],
    };

    public Task OnConnectedAsync(ServiceSession session, CancellationToken cancellationToken)
    {
        // Another store epoch: the index of the old one means nothing any more (LOCAL-SPEC.md §9.3).
        if (session.CatchUp.DiscardIndex)
            Interlocked.Exchange(ref count, 0);
        return Task.CompletedTask;
    }

    // Once per storing, in store order; the runtime advances its cursor after this returned.
    public async Task OnEntryAsync(ServiceSession session, EntryStored stored, CancellationToken cancellationToken)
    {
        long now = Interlocked.Increment(ref count);
        byte[] payload = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(payload, now);
        await session.EmitAsync("counted", stored.Entry.PackageId, payload, cancellationToken);
    }

    public Task OnPackageChangedAsync(ServiceSession session, PackageChanged changed, CancellationToken cancellationToken) => Task.CompletedTask;

    // An app's ServiceCall("counter", "count", ...): answered off the read loop, within 60 seconds.
    public ValueTask<CallReply> OnCallAsync(ServiceSession session, ForwardedCall call, CancellationToken deadline)
    {
        if (call.Method != "count")
            return ValueTask.FromResult(CallReply.Failure((Status)32_768, new Error { Message = $"No method '{call.Method}'.", Detail = "method" }));
        byte[] reply = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(reply, Interlocked.Read(ref count));
        return ValueTask.FromResult(CallReply.Ok(reply));
    }

    public Task OnDisconnectedAsync(ServiceSession session, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

The Host runs it once its `config.json` lists it (an absolute path, or a bare name found beside `axiom-meshd`):

```text
"services": { "counter": { "spawn": "counter", "kinds": [100], "entryKinds": [100] } }
```

Keep the service's rows in its database: `session.State.Database` is an LDB `Database`; declare `[LdbEntity]` classes in
the service's own assembly and `LdbContext.EnsureTableAsync` them in `OnConnectedAsync`. Rows of two assemblies share one
database (the runtime's cursor is one of them).

## What the runtime guarantees

- Every Entry of the service's kinds reaches `OnEntryAsync` once per storing, in store order, across reconnects and
  restarts; the cursor never moves past an Entry before its handler returned, so a crash may repeat Entries and never
  skips one. Dedupe by the Entry's hash. The cursor is committed lazily durable (LDB `Durability.Lazy`: a crash loses only
  the latest commits, in order) after each page, at each lull of the live stream, every 64 Entries and when the session
  ends; commit the service's own rows the same way or more durably.
- More than 256 Entries behind, it pages with `ListStored` before it subscribes (`CatchUpStep`) and hands each page of up
  to 64 Entries to `OnEntriesAsync` (by default `OnEntryAsync` for each; override it to write a page in one transaction);
  a lapsed subscription is renewed from the next store sequence; another store epoch tells the service to discard its
  index.
- Calls run off the read loop with their 60-second deadline; each is answered exactly once, never later than 120 seconds
  after it arrived (LOCAL-SPEC.md §9.8).
- `ServiceSession.Identity` is the Host's identity as it stands (`null` while it has none), kept current over topic `users`
  and `GetIdentity`: a service the Host spawned before `identity create` sees the identity once it exists, and every
  Roster after it. `ServiceSession.Host` is the `HelloAck` of the connection, for the store epoch and sequence; its
  identity is the one at connect only.
- A lost Host is reconnected 1 s, 2 s, 4 s ... 30 s later (`ReconnectRule`); a refused name, token or version exits with
  `ServiceExitCode.Refused`, a missing token or an unusable definition with `Configuration`, a failing handler or state with
  `State`, so the Host's supervisor knows what to do. A spawned service exits `Ok` when its Host drains; one run by hand
  waits for its Host to come back.

## Testing a service

`Axiom.Mesh.Testing` runs a service in process beside an in-memory node: configure the node with
`TestServices.With(TestCliService.Local, definition)`, then `await node.RunServiceAsync(service, ct)`; its state lives in
memory, kept by the node across runs, on the mesh's fake clock. `TestEchoService` is the smallest service there is.

## Learn more

- [LOCAL-SPEC.md](../Documentation/LOCAL-SPEC.md) §9 (the service role), §4.2 (spawned services and their environment),
  §6.7 and §9.8 (calls), §9.10 (events).
- [DESIGN.md](../Documentation/DESIGN.md) §10.3 and §10.5: services as programs of their own, owning no user interface.
- `AI/Axiom.Mesh.Service.SKILL.md` for an agent writing a service.
