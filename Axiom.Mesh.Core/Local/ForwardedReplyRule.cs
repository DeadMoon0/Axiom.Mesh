using System;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// What the caller of a <c>ServiceCall</c> hears, from how its <c>ForwardedCall</c> ended (LOCAL-SPEC.md §6.7, §9.8): the
/// service's payload; its status with its <c>Error</c>, a service-defined code included; <c>ServiceTimeout</c> at the
/// deadline; <c>ServiceUnavailable</c> when the service's connection ended first, or when its reply is not the callback's
/// layout or carries a reserved status, which is also a violation of the service's connection.
/// </summary>
internal static class ForwardedReplyRule
{
    /// <summary>Judges one outcome.</summary>
    /// <param name="correlation">The caller's correlation.</param>
    /// <param name="service">The service's name, for the messages.</param>
    /// <param name="outcome">How the callback ended.</param>
    /// <param name="violation">Set to the Bye detail when the reply is a violation of the service's connection.</param>
    /// <returns>The caller's answer.</returns>
    public static LocalAnswer Judge(uint correlation, string service, CallbackOutcome outcome, out string? violation)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        violation = null;
        switch (outcome.End)
        {
            case CallbackEnd.Expired:
                return LocalAnswer.Failure(correlation, Status.ServiceTimeout, $"The service {service} did not reply within {Limits.ServiceCallSeconds} seconds; whatever it did meanwhile stays done.", string.Empty);
            case CallbackEnd.Replied:
                break;
            default:
                return Unavailable(correlation, service, "ended its connection before it replied");
        }

        Reply reply = outcome.Reply!;
        if (reply.IsOk && reply.TryReadOk<ForwardedCall, ReadOnlyMemory<byte>>(out ReadOnlyMemory<byte> payload))
            return LocalAnswer.Ok<ServiceCall, ReadOnlyMemory<byte>>(correlation, payload);
        if (!reply.IsOk && ReplyStatusRule.MayReply(reply.Status) && reply.TryReadError(out Error? error))
            return LocalAnswer.Failure(correlation, reply.Status, error);

        violation = reply.IsOk || ReplyStatusRule.MayReply(reply.Status)
            ? $"the Reply to callback {reply.CallbackId} is not the layout of ForwardedCall's reply"
            : $"the Reply to callback {reply.CallbackId} carries the reserved status {(ushort)reply.Status}";
        return Unavailable(correlation, service, "sent a reply the Host cannot pass on and was disconnected");
    }

    /// <summary>The answer for a service that is not configured or not connected, at once (§6.7).</summary>
    /// <param name="correlation">The caller's correlation.</param>
    /// <param name="service">The service's name.</param>
    /// <param name="why">What happened, after the service's name.</param>
    /// <returns>The answer.</returns>
    public static LocalAnswer Unavailable(uint correlation, string service, string why) =>
        LocalAnswer.Failure(correlation, Status.ServiceUnavailable, $"The service {service} {why}; the Host restarts a service it supervises, so try again shortly.", string.Empty);
}
