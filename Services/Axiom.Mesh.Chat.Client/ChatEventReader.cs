using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat;

/// <summary>
/// Reads the events of <c>axiom-mesh-chat/1</c> from topic <c>service</c> (LOCAL-SPEC.md §7.3 <c>ServiceEvent</c>,
/// CHAT-SERVICE.md §6). An app subscribes with <c>Filter.Service = "chat"</c> (and a Package, to watch one Channel) and
/// hands every event here.
/// </summary>
public static class ChatEventReader
{
    /// <summary>The event a <c>ServiceEvent</c> carries, when it is one of the chat service's that this library knows.</summary>
    /// <param name="received">The event as the Host delivered it.</param>
    /// <param name="chat">The chat event, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="false"/> for another service's event, an event name this library does not know (a later version's;
    /// ignore it) or a payload that does not parse.
    /// </returns>
    public static bool TryRead(ServiceEvent received, [NotNullWhen(true)] out ChatEvent? chat)
    {
        ArgumentNullException.ThrowIfNull(received);
        chat = null;
        if (received.Service != ChatProtocol.ServiceName)
            return false;
        chat = received.Event switch
        {
            ChatEvents.Message => ChatCodec.TryDecode(received.Payload, out MessageEvent? message) ? message : null,
            ChatEvents.Withdrawn => ChatCodec.TryDecode(received.Payload, out WithdrawnEvent? withdrawn) ? withdrawn : null,
            ChatEvents.Channel => ChatCodec.TryDecode(received.Payload, out ChannelEvent? channel) ? channel : null,
            ChatEvents.Job => ChatCodec.TryDecode(received.Payload, out JobEvent? job) ? job : null,
            _ => null,
        };
        return chat is not null;
    }

    /// <summary>The filter that subscribes to every chat event, or to one Channel's and the jobs' (a zero Package).</summary>
    /// <param name="channel">The Channel; zero for every Channel.</param>
    /// <returns>The filter for topic <c>service</c>.</returns>
    /// <remarks>A filter with a Package receives only that Package's events; <c>job</c> events carry none, so a watcher of jobs subscribes with zero.</remarks>
    public static Filter FilterOf(PackageId channel) => new() { Service = ChatProtocol.ServiceName, PackageId = channel };
}
