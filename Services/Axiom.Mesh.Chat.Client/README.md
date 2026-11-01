# Axiom.Mesh.Chat.Client

The chat service's namespace, `axiom-mesh-chat/1`, typed for every app (S-D7): the records of its methods and events,
its statuses and bounds, one call helper over `ServiceCall` and one reader of its events. The command line's `chat …`
commands are built on it, and the app's chat view will be. Part of the
[Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh) family.

Namespace: `Axiom.Mesh.Chat`. Depends on `Axiom.Mesh.Local` (the local protocol and its client) and so on
`Axiom.Mesh.Model`; never on the chat service's executable, the engine or the daemon. The namespace is specified in
[CHAT-SERVICE.md](../../Documentation/CHAT-SERVICE.md).

## Status

0.2.0, new: draft 1 of the namespace. Ten methods (`about`, `channels`, `timeline`, `message`, `thread`, `send`,
`withdraw`, `attach`, `get`, `jobs`), four events (`message`, `withdrawn`, `channel`, `job`), six statuses from 32 768
(`ChatStatus`).

## Quick Start

A user client calls a method with `ChatCall.SendAsync`: the request names its reply's type, so a request and another
method's reply cannot be paired. A failure is the call's status: one of LOCAL-SPEC.md §11.1 (`ServiceUnavailable` when the
chat service is not connected) or a `ChatStatus`.

```csharp
using Axiom.Mesh;
using Axiom.Mesh.Chat;
using Axiom.Mesh.Local;

public static class ChatQuickStart
{
    // Sends a message to a Channel and prints the Channel's newest page, withdrawn messages without their text.
    public static async Task SayHelloAsync(LocalClient user, PackageId channel, CancellationToken ct)
    {
        LocalResult<SendReply> sent = await ChatCall.SendAsync(user, new SendRequest { PackageId = channel, Text = "hello" }, ct);
        if (!sent.IsOk)
        {
            string why = ChatStatusText.IsChatStatus(sent.Status, out ChatStatus chat) ? ChatStatusText.Explain(chat) : sent.Status.ToString();
            Console.WriteLine($"not sent: {why}");
            return;
        }

        LocalResult<TimelineReply> page = await ChatCall.SendAsync(user, new TimelineRequest { PackageId = channel, Reverse = true, Limit = 20 }, ct);
        foreach (MessageView message in page.Value.Messages)
            Console.WriteLine($"{message.Author.UserName}: {(message.Withdrawn ? "(withdrawn)" : message.Text)}");
    }
}
```

Events arrive on topic `service`; subscribe before you act, then read them with `ChatEventReader`:

```csharp
using Axiom.Mesh;
using Axiom.Mesh.Chat;
using Axiom.Mesh.Local;

public static class ChatWatch
{
    public static async Task WatchAsync(LocalClient user, PackageId channel, CancellationToken ct)
    {
        LocalResult<Subscription<ServiceEvent>> subscribed = await user.SubscribeAsync<ServiceEvent>(ChatEventReader.FilterOf(channel), ct);
        await foreach (ServiceEvent received in subscribed.Value.WithCancellation(ct))
        {
            if (ChatEventReader.TryRead(received, out ChatEvent? chat) && chat is MessageEvent message)
                Console.WriteLine($"{message.Author.UserName}: {message.Preview}");
        }
    }
}
```

An attachment is a job: `attach` answers a job number at once, and the `job` event with that number and state `Done`
carries the `Attachment` to put into `send`. Subscribe with a zero Package to see `job` events (they concern no Channel).

## Rules of use

- A withdrawn or broken `MessageView` carries no text and no attachments; show `(withdrawn)` or `(broken)`, never guess.
- `send` and `withdraw` are not idempotent; every other method is. A `ServiceTimeout` says nothing about whether a
  `send` took effect: look at the timeline before sending again.
- Wait for `about.CaughtUp` before reading a timeline you expect to be complete, never for time.
- `ChatCodec` reads and writes every record; `Documentation/vectors/chat/records.txt` holds a sample of each.

## Learn more

- [CHAT-SERVICE.md](../../Documentation/CHAT-SERVICE.md): records, methods, events, statuses, jobs, limits.
- `AI/Axiom.Mesh.Chat.Client.SKILL.md` for an agent writing an app over the chat namespace.
