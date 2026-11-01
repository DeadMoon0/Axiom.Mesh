# Axiom.Mesh.Sync.Client

The sync service's namespace, `axiom-mesh-sync/1`, typed for every app (S-D7): the records of its methods and events,
its statuses and bounds, one call helper over `ServiceCall` and one reader of its events. The command line's `sync …`
commands are built on it, and the app's sync view will be. Part of the
[Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh) family.

Namespace: `Axiom.Mesh.Sync`. Depends on `Axiom.Mesh.Local` (the local protocol and its client) and so on
`Axiom.Mesh.Model`; never on the sync service's executable, the engine or the daemon. The namespace is specified in
[SYNC-SERVICE.md](../../Documentation/SYNC-SERVICE.md).

## Status

0.2.0, new: draft 1 of the namespace. Six methods (`bindings`, `bind`, `unbind`, `status`, `scan`, `conflicts`), two
events (`binding`, `conflict`), seven statuses from 32 768 (`SyncStatus`).

## Quick Start

A user client calls a method with `SyncCall.SendAsync`: the request names its reply's type, so a request and another
method's reply cannot be paired. A failure is the call's status: one of LOCAL-SPEC.md §11.1 (`ServiceUnavailable` when the
sync service is not connected) or a `SyncStatus`.

```csharp
using Axiom.Mesh;
using Axiom.Mesh.Local;
using Axiom.Mesh.Sync;

public static class SyncQuickStart
{
    // Binds a folder to a followed Archive, asks for a run now, and prints where the binding stands.
    public static async Task KeepInSyncAsync(LocalClient user, string folder, PackageId archive, CancellationToken ct)
    {
        LocalResult<BindReply> bound = await SyncCall.SendAsync(user, new BindRequest { Directory = folder, PackageId = archive, Ignore = ["*.tmp"] }, ct);
        if (!bound.IsOk)
        {
            string why = SyncStatusText.IsSyncStatus(bound.Status, out SyncStatus sync) ? SyncStatusText.Explain(sync) : bound.Status.ToString();
            Console.WriteLine($"not bound: {why}");
            return;
        }

        await SyncCall.SendAsync(user, new ScanRequest { Directory = folder }, ct);
        LocalResult<StatusReply> status = await SyncCall.SendAsync(user, new StatusRequest { Directory = folder }, ct);
        Binding binding = status.Value.Bindings[0];
        Console.WriteLine($"{binding.Directory}: {binding.State}, {binding.Files} files, {binding.Leaves} leaves, {binding.Conflicts} conflicts");
    }
}
```

Events arrive on topic `service`; subscribe before you act, then read them with `SyncEventReader`:

```csharp
using Axiom.Mesh.Local;
using Axiom.Mesh.Sync;

public static class SyncWatch
{
    public static async Task WatchAsync(LocalClient user, CancellationToken ct)
    {
        LocalResult<Subscription<ServiceEvent>> subscribed = await user.SubscribeAsync<ServiceEvent>(SyncEventReader.Filter, ct);
        await foreach (ServiceEvent received in subscribed.Value.WithCancellation(ct))
        {
            if (SyncEventReader.TryRead(received, out SyncEvent? sync) && sync is ConflictEvent conflict)
                Console.WriteLine($"conflict at {conflict.Conflict.Path}; the other side's copy is {conflict.Conflict.CopyPath}");
        }
    }
}
```

## Rules of use

- Every method answers at once from the service's state; the run it starts reports itself with `binding` events, and
  `Binding.Pending` says whether one is still queued or running. Wait on that, never on time.
- The service follows no Archive itself: follow it (or create it) as the user first, then `bind`.
- There is no method to resolve a conflict: delete the copy you do not want; the next run publishes that.
- A read-only binding (`Rights` below `Write`) pulls and never publishes; `LocalChanges` counts what it keeps.
- `SyncCodec` reads and writes every record; `Documentation/vectors/sync/records.txt` holds a sample of each.

## Learn more

- [SYNC-SERVICE.md](../../Documentation/SYNC-SERVICE.md): records, methods, events, statuses, the run and its merges.
- `AI/Axiom.Mesh.Sync.Client.SKILL.md` for an agent writing an app over the sync namespace.
