using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// <c>Reply</c> (33, LOCAL-SPEC.md §3.6): a service's one answer to a <see cref="Callback"/>, carrying the callback id, a
/// status and either the callback's reply body or an <see cref="Error"/>. A user client may not send one.
/// </summary>
public sealed class Reply : LocalMessage, IClientMessage
{
    private readonly object? value;
    private readonly BodyWriter? writeValue;

    private Reply(uint callbackId, Status status, ReadOnlyMemory<byte> body, object? value, BodyWriter? writeValue)
    {
        if (callbackId == 0)
            throw new ArgumentException("A callback id is never 0 (LOCAL-SPEC.md §3.2).", nameof(callbackId));
        CallbackId = callbackId;
        Status = status;
        Body = body;
        this.value = value;
        this.writeValue = writeValue;
    }

    /// <summary>The callback it answers.</summary>
    public uint CallbackId { get; }

    /// <summary>The status: <c>Ok</c>, a code of §11.1, or a service-defined code from 32 768 (§11.2).</summary>
    public Status Status { get; }

    /// <summary>Whether the callback succeeded.</summary>
    public bool IsOk => Status == Status.Ok;

    /// <summary>The body as received, a view valid until the next message is read; empty for a reply built to be sent.</summary>
    public ReadOnlyMemory<byte> Body { get; }

    /// <inheritdoc />
    public override ushort MessageId => LocalMessageIds.Reply;

    /// <summary>The success of <typeparamref name="TCallback"/> with its reply body.</summary>
    /// <typeparam name="TCallback">The callback answered.</typeparam>
    /// <typeparam name="TReply">Its reply body.</typeparam>
    /// <param name="callbackId">The callback's id.</param>
    /// <param name="reply">The reply body.</param>
    /// <returns>The reply.</returns>
    public static Reply Ok<TCallback, TReply>(uint callbackId, TReply reply)
        where TCallback : LocalCallback, ICallback<TCallback, TReply> =>
        new(callbackId, Status.Ok, default, reply, ReplyWriter<TCallback, TReply>.Write);

    /// <summary>A failure with a status other than <see cref="Status.Ok"/> and the <see cref="Error"/> that explains it.</summary>
    /// <param name="callbackId">The callback's id.</param>
    /// <param name="status">Why it failed.</param>
    /// <param name="error">The explanation.</param>
    /// <returns>The reply.</returns>
    /// <exception cref="ArgumentException"><paramref name="status"/> is <see cref="Status.Ok"/>.</exception>
    public static Reply Failure(uint callbackId, Status status, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (status == Status.Ok)
            throw new ArgumentException("A failure needs a status other than Ok; use Reply.Ok for a success.", nameof(status));
        return new Reply(callbackId, status, default, error, WriteError);
    }

    /// <summary>The answer to a callback id this service does not know (§1.5): status <c>UnknownRequest</c>.</summary>
    /// <param name="callbackId">The callback's id.</param>
    /// <returns>The reply.</returns>
    public static Reply ForUnknownCallback(uint callbackId) =>
        Failure(callbackId, Status.UnknownRequest, new Error { Message = "This service does not know the callback." });

    /// <summary>Reads the reply body as the one of <typeparamref name="TCallback"/>, exactly to its end.</summary>
    /// <typeparam name="TCallback">The callback this answers.</typeparam>
    /// <typeparam name="TReply">Its reply body.</typeparam>
    /// <param name="reply">The reply body.</param>
    /// <returns><see langword="false"/> when the status is not <c>Ok</c> or the body is not that layout.</returns>
    public bool TryReadOk<TCallback, TReply>([MaybeNullWhen(false)] out TReply reply)
        where TCallback : LocalCallback, ICallback<TCallback, TReply>
    {
        reply = default;
        if (!IsOk)
            return false;
        if (writeValue is not null)
        {
            if (value is not TReply built)
                return false;
            reply = built;
            return true;
        }

        var reader = new WireReader(Body);
        return TCallback.TryReadReply(ref reader, out reply) && reader.IsAtEnd;
    }

    /// <summary>Reads the <see cref="Error"/> of a failure, exactly to its end.</summary>
    /// <param name="error">The error.</param>
    /// <returns><see langword="false"/> when the status is <c>Ok</c> or the body is not an <see cref="Error"/>.</returns>
    public bool TryReadError([NotNullWhen(true)] out Error? error)
    {
        error = null;
        if (IsOk)
            return false;
        if (writeValue is not null)
        {
            error = value as Error;
            return error is not null;
        }

        var reader = new WireReader(Body);
        error = Error.TryRead(ref reader, out Error? read) && reader.IsAtEnd ? read : null;
        return error is not null;
    }

    /// <summary>
    /// The same reply with its body copied, so that it outlives the next message read: for a reply handed to another task
    /// than the one reading the connection. A reply built to be sent is returned as it is.
    /// </summary>
    /// <returns>A reply whose <see cref="Body"/> no read invalidates.</returns>
    public Reply Detach() => writeValue is not null ? this : new Reply(CallbackId, Status, Body.ToArray(), null, null);

    /// <inheritdoc />
    public override string ToString() => $"!{CallbackId} {Status}";

    internal static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Reply? message)
    {
        uint callbackId = reader.UInt32BigEndian();
        var status = (Status)reader.UInt16BigEndian();
        ReadOnlyMemory<byte> body = reader.RestView();
        message = reader.Ok && callbackId != 0 ? new Reply(callbackId, status, body, null, null) : null;
        return message is not null;
    }

    internal override void WritePayload(ref WireWriter writer)
    {
        writer.UInt32BigEndian(CallbackId);
        writer.UInt16BigEndian((ushort)Status);
        if (writeValue is not null)
            writeValue(value!, ref writer);
        else
            writer.Bytes(Body.Span);
    }

    private static void WriteError(object error, ref WireWriter writer) => ((Error)error).Write(ref writer);
}
