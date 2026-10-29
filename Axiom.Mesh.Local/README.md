# Axiom.Mesh.Local

`axiom-mesh-local/1` ([LOCAL-SPEC.md](https://github.com/DeadMoon0/Axiom.Mesh/blob/main/Documentation/LOCAL-SPEC.md)),
the protocol every program on a device speaks to the [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh) Host over the
pipe, in both roles (user and service), with the client library. The one package an app, a script or a service
references: it never reaches the Host's engine, a key or the database.

Namespace: `Axiom.Mesh.Local`. Depends on `Axiom.Mesh.Model` and `Axiom.ProtoStream`, nothing else.

## Quick Start

Connect as a user, ask for the identity, then subscribe to Packages and list them:

```csharp
using Axiom.Mesh;
using Axiom.Mesh.Local;

CancellationToken ct = CancellationToken.None;

// The pipe of the Host this process talks to: AXIOM_MESH_PIPE, else the platform's default (LOCAL-SPEC.md §2.1).
Stream pipe = await LocalPipe.ConnectAsync(LocalPipe.Locate(), ct);
await using LocalClient client = await LocalClient.OpenAsync(pipe, new LocalClientOptions { Name = "quick-start" }, ct);

// A request is a value in and a value out: the OK body, or the status the Host refused it with. Never an exception.
LocalResult<Identity> identity = await client.SendAsync(new GetIdentity(), ct);
if (!identity.IsOk)
{
    Console.WriteLine($"{identity.Status}: {identity.Error.Message}");
    return;
}

Console.WriteLine($"{identity.Value.UserName} on {identity.Value.DeviceName}");

// Subscribe first, then list (§3.4): every change after the subscription arrives as an event, none is missed.
LocalResult<Subscription<PackageChanged>> subscribed = await client.SubscribeAsync<PackageChanged>(Filter.Any, ct);
await using Subscription<PackageChanged> packages = subscribed.Value;

LocalResult<PackagePage> page = await client.SendAsync(new ListPackages { FollowedOnly = true }, ct);
foreach (Package package in page.Value.Packages)
    Console.WriteLine($"{package.Name} ({package.Kind}, {package.Entries} entries)");

// The enumeration ends when the subscription does: unsubscribed, or lapsed for reading too slowly (see Lapse).
await foreach (PackageChanged changed in packages)
    Console.WriteLine($"{changed.Change}: {changed.Package.Name}");
```

The same client over an in-memory pipe pair, as a test or an app that runs the Host in its own process uses it; the
Host adopts the other end:

```csharp
using System.IO.Pipelines;
using Axiom.Mesh.Local;

var toHost = new Pipe();
var toClient = new Pipe();
var clientEnd = new PipeEnd(toClient.Reader, toHost.Writer);
var hostEnd = new PipeEnd(toHost.Reader, toClient.Writer);

await using LocalClient client = await LocalClient.OpenAsync(clientEnd, new LocalClientOptions { Name = "in-memory" }, CancellationToken.None);
Console.WriteLine($"Connected as {client.Host.Role}; the Host's store is at sequence {client.Host.StoreSeq}.");

sealed record PipeEnd(PipeReader Input, PipeWriter Output) : IDuplexPipe;
```

A service connects the same way with `Role = Role.Service`, its configured `Name` and its `Token`
(`ServiceToken.TryParseHex` of `AXIOM_MESH_SERVICE_TOKEN` when the Host spawned it, or of the token the Host's
configuration holds for an external service), then sends `RegisterKind`.

## What is in it

| Area | Types |
|---|---|
| Client | `LocalClient` (`OpenAsync`, `SendAsync`, `SubscribeAsync`, `UnsubscribeAsync`, `ByeAsync`, `Host`, `Draining`, `Closed`), `LocalClientOptions`, `LocalResult<TOk>`, `Subscription<TEvent>`, `Reconnect.Delay`, `IndexCatchUp`, `LocalConnectionException`, `RequestLimitException` |
| Pipe | `LocalPipe` (`Locate`, `ConnectAsync`: the one place the package touches the OS), `PipeLocation.Resolve`, `PipeEnvironment`, `LocalPlatform`, `PipeKind` |
| Requests | one sealed record per request of §6 to §10 (58, every id of §14.1), each an `IRequest<TSelf, TOk>` naming its id, roles and OK body; `RequestTable` (id → name, section, roles, body reader) for the Host's dispatcher |
| Events, callbacks | one sealed record per event of §7.3 (`IEvent<TSelf>`, with its `Topic`), `ForwardedCall` (`ICallback<TSelf, TReply>`) |
| Records | every record of §5 (`Identity`, `User`, `Device`, `Package`, `Grant`, `Entry`, `Peer`, `Session`, `StaticPeer`, `Store`, `Pin`, `PinKey`, `Service`, `Operation`, `Error`, `Pairing`, `Transfer`, `TelemetrySeries`, `TelemetryPoint`, `Filter`) and the OK bodies with several fields (`PackagePage`, `EntryPage`, …) |
| Frames | `IClientMessage` / `IHostMessage`: `Hello`, `HelloAck`, `Bye`, `Ping`, `Pong`, `Drain`, `RequestFrame`, `Response`, `EventFrame`, `Callback`, `Reply`, and `UnknownRequest` / `UnknownEvent` / `UnknownCallback` for ids a side does not know; `ServiceToken`, `LocalMessageIds`, `MessageClass` |
| Protocol | `LocalProtocol.HostSide` and `LocalProtocol.ClientSide`, the ProtoStream definitions; the enumerations of §14 (`Status`, `Topic`, `ByeReason`, `PinReason`, …) |

## Rules of use

- **Refusals are values.** `SendAsync` returns a `LocalResult<TOk>`; check `IsOk`, then read `Value` or `Status` and
  `Error`. Exceptions are for the connection only (`LocalConnectionException`) and for a caller that would break a
  limit (`RequestLimitException`: at most 64 requests outstanding per connection).
- **Subscribe, then list.** Events reflect changes after the `Subscribe` response; reconcile a listing with the events
  by `manifestSeq`, `rosterSeq`, `storeSeq` or hashes (§3.4).
- **A slow reader loses its subscription, not the connection.** Each subscription queues at most 256 events; past that
  it ends with `Lapse` set (`Dropped`, and for topic entries `LastSeq`, so re-subscribe with `FromSeq = LastSeq + 1`).
- **Mind the store epoch.** An index built under another `HelloAck.StoreEpoch` is discarded: `IndexCatchUp.Plan` says
  so and where to start (§9.3).
- **Reconnect with backoff.** `Reconnect.Delay(attempt)` is 1 s doubling to 30 s; re-subscribe and, for a service,
  `RegisterKind` again. `WriteEntry` is not idempotent: catch up before retrying a write whose response was lost.
- **Never display a redacted Entry's payload** (`Entry.Redacted`, §5.6).
- **Unknown values are kept.** An enumerated value a newer Host sends is read and kept as its raw value (§1.5).
