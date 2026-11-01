# Axiom.Mesh.Chat

`axiom-mesh-chat`, the chat service of [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh): Channels between users,
messages with attachments and replies, withdrawals, indexed in a database of its own and offered to every app as the
namespace `axiom-mesh-chat/1` ([CHAT-SERVICE.md](../../Documentation/CHAT-SERVICE.md)). It owns no user interface
(S-D13): the command line's `chat …` commands and the app are apps over `Axiom.Mesh.Chat.Client`.

An executable, shipped beside `axiom-meshd` in the release zips (Native AOT, `TargetName` `axiom-mesh-chat`), never a
NuGet package. Built on `Axiom.Mesh.Service`; references `Axiom.Mesh.Local`, `Axiom.Mesh.Model`, `Axiom.Mesh.Chat.Client`
and LDB, never the engine or the daemon.

## Status

0.2.0, new: draft 1 of the namespace. The Host's default `config.json` lists `services.chat` with
`spawn: "axiom-mesh-chat"` (kinds [2], entry kinds [2, 3]); a bare name resolves beside `axiom-meshd`, and an absent
executable is noted once and not supervised (S-D11).

## Quick Start

Nothing to start: the Host spawns the service, restarts it after a crash, and stops it with itself. Use it through the
command line:

```text
axiom-mesh package create Family
axiom-mesh chat send Family --text "holiday" --file photo.jpg
axiom-mesh chat show Family
axiom-mesh chat get 3f2a photo.jpg --out photo.jpg
axiom-mesh chat withdraw 3f2a
axiom-mesh chat watch Family
```

To run it by hand against a Host (an external service, LOCAL-SPEC.md §4.2), list it in the Host's `config.json` with a
token and start `axiom-mesh-chat run`; `axiom-mesh-chat version` prints its version.

In process, beside a test node, it is one call; its state lives in memory, kept by the node:

```csharp
using Axiom.Mesh;
using Axiom.Mesh.Chat;
using Axiom.Mesh.Local;
using Axiom.Mesh.Testing;

public static class ChatInATest
{
    public static async Task RunAsync(CancellationToken ct)
    {
        await using TestMesh mesh = await TestMesh.StartAsync(
            TestMeshOptions.Of("alice-laptop") with { Local = TestServices.With(TestCliService.Local, ChatProgram.Definition) }, ct);
        await using RunningService chat = await mesh["alice-laptop"].RunServiceAsync(new ChatService(mesh.Clock), ct);
        await using LocalClient app = await mesh["alice-laptop"].ConnectUserAsync("my-app", ct);
        LocalResult<AboutReply> about = await ChatCall.SendAsync(app, new AboutRequest(), ct);
        Console.WriteLine($"{about.Value.Namespace}, caught up: {about.Value.CaughtUp}");
    }
}
```

## What the service guarantees

- Both devices of a Channel show the same messages in the same order (SPEC.md §9.3); a withdrawn message is never shown,
  whichever of it and its Redaction arrived first (SPEC.md §9.4); a broken one neither (PAYLOADS.md §1.2).
- Attachments written by `get` are byte-equal to what was attached, and never partial under their name.
- After a restart it catches up by store sequence without a duplicate; under another store epoch it discards its index.
- A withdrawal SPEC.md §9.4 would not honour is refused (`NotYours`) before anything is written.

## Learn more

- [CHAT-SERVICE.md](../../Documentation/CHAT-SERVICE.md): the namespace, the index, the rules, the jobs.
- [Axiom.Mesh.Service](../../Axiom.Mesh.Service/README.md): the runtime it is built on.
- `AI/Axiom.Mesh.Chat.SKILL.md` for an agent changing the service.
