# Axiom.Mesh.Sync

`axiom-mesh-sync`, the sync service of [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh): a folder bound to an Archive
and kept in step with one of its branches both ways. Local changes are published as Versions; the branch's leaves are merged
by the rules of SPEC.md §9.5 and written into the folder; a path changed on two devices keeps the later side's file and the
other side's copy beside it. Bindings and the Version DAG live in a database of its own; the namespace `axiom-mesh-sync/1`
([SYNC-SERVICE.md](../../Documentation/SYNC-SERVICE.md)) is its only surface. It owns no user interface (S-D13): the
command line's `sync …` commands and the app are apps over `Axiom.Mesh.Sync.Client`.

An executable, shipped beside `axiom-meshd` in the release zips (Native AOT, `TargetName` `axiom-mesh-sync`), never a
NuGet package. Built on `Axiom.Mesh.Service`; references `Axiom.Mesh.Local`, `Axiom.Mesh.Model`, `Axiom.Mesh.Sync.Client`
and LDB, never the engine or the daemon.

## Status

0.2.0, new: draft 1 of the namespace. The Host's default `config.json` lists `services.sync` with
`spawn: "axiom-mesh-sync"` (kinds [1], entry kinds [1]); a bare name resolves beside `axiom-meshd`, and an absent
executable is noted once and not supervised (S-D11). `axiom-mesh host install` adds the entry to a `config.json` an older
Host wrote without it.

## Quick Start

Nothing to start: the Host spawns the service, restarts it after a crash, and stops it with itself. Use it through the
command line, on two PCs that are paired:

```text
PC A:  axiom-mesh sync add D:\shared --create Shared
PC B:  axiom-mesh sync add ~/shared --package Shared
       axiom-mesh sync status ~/shared
       axiom-mesh sync conflicts
       axiom-mesh sync watch
       axiom-mesh sync remove ~/shared        (the files stay)
```

To run it by hand against a Host (an external service, LOCAL-SPEC.md §4.2), list it in the Host's `config.json` with a
token and start `axiom-mesh-sync run`; `axiom-mesh-sync version` prints its version.

In process, beside a test node, it is one call; its state lives in memory, kept by the node. A test moves the debounce with
the mesh's fake clock and reads files at once with a stability of zero:

```csharp
using Axiom.Mesh;
using Axiom.Mesh.Local;
using Axiom.Mesh.Sync;
using Axiom.Mesh.Testing;

public static class SyncInATest
{
    public static async Task RunAsync(CancellationToken ct)
    {
        await using TestMesh mesh = await TestMesh.StartAsync(
            TestMeshOptions.Of("alice-laptop") with { Local = TestServices.With(TestCliService.Local, SyncProgram.Definition) }, ct);
        var options = new SyncServiceOptions { Time = mesh.Clock, Stability = TimeSpan.Zero };
        await using RunningService sync = await mesh["alice-laptop"].RunServiceAsync(new SyncService(options), ct);
        await using LocalClient app = await mesh["alice-laptop"].ConnectUserAsync("my-app", ct);
        LocalResult<BindingsReply> bindings = await SyncCall.SendAsync(app, new BindingsRequest(), ct);
        Console.WriteLine($"{bindings.Value.Bindings.Count} folders kept in sync");
    }
}
```

## What the service guarantees

- A folder bound on two devices holds the same files, byte-equal, once both ran; the same leaves merge to the same Tree on
  every device.
- A path changed differently on two devices loses nothing: the later leaf's file stands and the other side's file, or whole
  subtree, lies beside it as `<name>.conflict-<8 hex of its device>` on both.
- A run stopped at any point never writes a second Version for the same Tree nor leaves a partial file under a name, and the
  next run writes only what is left.
- A pull never overwrites or deletes a file changed in the folder since the last run; a read-only binding never publishes.

## Learn more

- [SYNC-SERVICE.md](../../Documentation/SYNC-SERVICE.md): bindings, the run and its invariants, merges, the namespace.
- [Axiom.Mesh.Service](../../Axiom.Mesh.Service/README.md): the runtime it is built on.
- `AI/Axiom.Mesh.Sync.SKILL.md` for an agent changing the service.
