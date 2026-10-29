using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Local.Tests.Support;

/// <summary>
/// One sample of every frame each side may send: every session message, a request frame for every request of
/// <see cref="RequestTable"/>, a response with every OK body, every event and the callback, plus the frames of ids the
/// receiver does not know.
/// </summary>
internal static class MessageSamples
{
    /// <summary>Every frame a client may send, by name.</summary>
    public static IReadOnlyDictionary<string, IClientMessage> FromClient { get; } = BuildFromClient();

    /// <summary>Every frame the Host may send, by name.</summary>
    public static IReadOnlyDictionary<string, IHostMessage> FromHost { get; } = BuildFromHost();

    private static Dictionary<string, IClientMessage> BuildFromClient()
    {
        var samples = new Dictionary<string, IClientMessage>(StringComparer.Ordinal)
        {
            ["Hello"] = Samples.Of<Hello>(),
            ["Hello (user, no token)"] = new Hello { Role = Role.User, Name = "app", ClientVersion = "1.0", Capabilities = ClientCapabilities.PartialTelemetry },
            ["Bye"] = Bye.Of(ByeReason.Done, "finished"),
            ["Ping"] = new Ping { Nonce = 42 },
            ["Pong"] = new Pong { Nonce = ulong.MaxValue },
            ["UnknownRequest"] = new UnknownRequest { Id = 999, Correlation = 3, Body = new byte[] { 1, 2, 3 } },
            ["Reply Ok"] = Reply.Ok<ForwardedCall, ReadOnlyMemory<byte>>(5, new byte[] { 9, 8, 7 }),
            ["Reply Failure"] = Reply.Failure(6, Status.NotFound, new Error { Message = "no such timeline", Detail = "channel" }),
            ["Reply service-defined"] = Reply.Failure(7, (Status)40_000, new Error { Message = "the service's own" }),
        };

        uint correlation = 100;
        foreach (RequestDescriptor request in RequestTable.Default.All)
            samples["Request " + request.Name] = new RequestFrame(correlation++, (LocalRequest)Samples.Of(request.RequestType));
        return samples;
    }

    private static Dictionary<string, IHostMessage> BuildFromHost()
    {
        var samples = new Dictionary<string, IHostMessage>(StringComparer.Ordinal)
        {
            ["HelloAck"] = Samples.Of<HelloAck>(),
            ["HelloAck (no identity)"] = new HelloAck { Role = Role.User, Identity = new Identity(), StoreEpoch = Samples.Hash(9) },
            ["Bye"] = Bye.Of(ByeReason.Shutdown, "stopping"),
            ["Ping"] = new Ping { Nonce = 1 },
            ["Pong"] = new Pong { Nonce = 2 },
            ["Drain"] = new Drain { GraceMilliseconds = 5_000 },
            ["Response Failure"] = Response.Failure(4, Status.NotOwner, new Error { Message = "only the owner", Detail = "packageId" }),
            ["Response UnknownRequest"] = Response.ForUnknownRequest(5),
            ["Response unknown status"] = Response.Failure(6, (Status)300, new Error { Message = "a newer Host's status" }),
            ["Callback ForwardedCall"] = new Callback(8, Samples.Of<ForwardedCall>()),
            ["UnknownEvent"] = new UnknownEvent { Id = 1100, SubscriptionId = 2, Body = new byte[] { 4, 5 } },
            ["UnknownCallback"] = new UnknownCallback { Id = 1600, CallbackId = 9, Body = new byte[] { 6 } },
        };

        uint correlation = 100;
        foreach (RequestDescriptor request in RequestTable.Default.All)
            samples["Response " + request.Name] = Samples.OkResponse(request, correlation++);

        foreach (Type @event in Samples.EventTypes())
            samples["Event " + @event.Name] = new EventFrame(3, (LocalEvent)Samples.Of(@event));
        return samples;
    }

    /// <summary>The names of the client's samples, for a theory.</summary>
    public static TheoryData<string> ClientNames() => [.. FromClient.Keys.Order(StringComparer.Ordinal)];

    /// <summary>The names of the Host's samples, for a theory.</summary>
    public static TheoryData<string> HostNames() => [.. FromHost.Keys.Order(StringComparer.Ordinal)];
}
