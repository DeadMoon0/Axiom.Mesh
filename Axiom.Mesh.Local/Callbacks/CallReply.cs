using System;
using System.Text;

namespace Axiom.Mesh.Local;

/// <summary>
/// What a service answers a callback with (LOCAL-SPEC.md §3.6, §9.8): its own payload, or a status and the
/// <see cref="Local.Error"/> that explains it. Only <see cref="Ok"/> and <see cref="Failure"/> make one, so a payload goes
/// with <c>Ok</c> only and a failure always carries a status <see cref="ReplyStatusRule"/> allows.
/// </summary>
public sealed class CallReply
{
    private CallReply(Status status, ReadOnlyMemory<byte> payload, Error? error)
    {
        Status = status;
        Payload = payload;
        Error = error;
    }

    /// <summary><c>Ok</c>, a code of §11.1, or a service-defined code from 32 768 (§11.2).</summary>
    public Status Status { get; }

    /// <summary>Whether the call succeeded.</summary>
    public bool IsOk => Status == Status.Ok;

    /// <summary>The service's reply payload; empty for a failure.</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>The explanation of a failure; <see langword="null"/> for a success.</summary>
    public Error? Error { get; }

    /// <summary>A success with the service's payload (at most 1 MiB, §3.2).</summary>
    /// <param name="payload">The payload, the service's own layout.</param>
    /// <returns>The reply.</returns>
    /// <exception cref="ArgumentException">The payload is larger than 1 MiB.</exception>
    public static CallReply Ok(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length > Limits.MaxPayloadLength)
            throw new ArgumentException($"A reply payload is at most {Limits.MaxPayloadLength} bytes, not {payload.Length} (LOCAL-SPEC.md §3.2).", nameof(payload));
        return new CallReply(Status.Ok, payload, null);
    }

    /// <summary>A failure: a status other than <c>Ok</c> that a reply may carry, and what it means here.</summary>
    /// <param name="status">A code of §11.1 except <c>Cancelled</c>, or a service-defined code from 32 768.</param>
    /// <param name="error">The explanation; its message is for display, its detail for logs.</param>
    /// <returns>The reply.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="status"/> is <c>Ok</c>, <c>Cancelled</c> or reserved, or the error's message is over 256 UTF-8 bytes or its
    /// detail over 1 024 (§5.14).
    /// </exception>
    public static CallReply Failure(Status status, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (Encoding.UTF8.GetByteCount(error.Message ?? "") > FieldBounds.ErrorMessageLength || Encoding.UTF8.GetByteCount(error.Detail ?? "") > FieldBounds.ErrorDetailLength)
            throw new ArgumentException($"An error's message is at most {FieldBounds.ErrorMessageLength} UTF-8 bytes and its detail at most {FieldBounds.ErrorDetailLength} (LOCAL-SPEC.md §5.14).", nameof(error));
        if (status == Status.Ok || !ReplyStatusRule.MayReply(status))
            throw new ArgumentException($"Status {(ushort)status} cannot fail a callback: use a code of LOCAL-SPEC.md §11.1 other than Ok and Cancelled, or a service-defined code from {ReplyStatusRule.FirstServiceDefined} (§11.2).", nameof(status));
        return new CallReply(status, ReadOnlyMemory<byte>.Empty, error);
    }

    /// <summary>The reply on the wire, for the callback it answers.</summary>
    /// <param name="callbackId">The callback's id.</param>
    /// <returns>The <c>Reply</c>.</returns>
    internal Reply ToReply(uint callbackId) =>
        IsOk ? Reply.Ok<ForwardedCall, ReadOnlyMemory<byte>>(callbackId, Payload) : Reply.Failure(callbackId, Status, Error!);

    /// <inheritdoc />
    public override string ToString() => IsOk ? $"Ok ({Payload.Length} bytes)" : $"{Status}: {Error!.Message}";
}
