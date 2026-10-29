using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// <c>Response</c> (32, LOCAL-SPEC.md §3.4): the Host's one answer to a request, carrying the request's correlation, a
/// status and either the request's OK body or an <see cref="Error"/>. A failed request has no effect on the Host.
/// </summary>
/// <remarks>
/// The Host builds one with <see cref="Ok{TRequest, TOk}"/> or <see cref="Failure"/>; a client receives one with the body
/// as bytes and reads it with <see cref="TryReadOk{TRequest, TOk}"/> or <see cref="TryReadError"/> (which
/// <c>LocalClient</c> does for you). A status this side does not know is tolerated (§1.5): its body is an
/// <see cref="Error"/> like any other failure's.
/// </remarks>
public sealed class Response : LocalMessage, IHostMessage
{
    private readonly object? value;
    private readonly BodyWriter? writeValue;

    private Response(uint correlation, Status status, ReadOnlyMemory<byte> body, object? value, BodyWriter? writeValue)
    {
        if (correlation == 0)
            throw new ArgumentException("A correlation is never 0 (LOCAL-SPEC.md §3.2).", nameof(correlation));
        Correlation = correlation;
        Status = status;
        Body = body;
        this.value = value;
        this.writeValue = writeValue;
    }

    /// <summary>The correlation of the request it answers.</summary>
    public uint Correlation { get; }

    /// <summary>The status (§11.1).</summary>
    public Status Status { get; }

    /// <summary>Whether the request succeeded.</summary>
    public bool IsOk => Status == Status.Ok;

    /// <summary>
    /// The body as received: the OK body, or an <see cref="Error"/>. A view of the frame, valid until the next message
    /// is read; empty for a response built to be sent.
    /// </summary>
    public ReadOnlyMemory<byte> Body { get; }

    /// <inheritdoc />
    public override ushort MessageId => LocalMessageIds.Response;

    /// <summary>The success of <typeparamref name="TRequest"/> with its OK body.</summary>
    /// <typeparam name="TRequest">The request answered.</typeparam>
    /// <typeparam name="TOk">Its OK body.</typeparam>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="ok">The OK body.</param>
    /// <returns>The response; the body is written when it is sent.</returns>
    public static Response Ok<TRequest, TOk>(uint correlation, TOk ok)
        where TRequest : LocalRequest, IRequest<TRequest, TOk> =>
        new(correlation, Status.Ok, default, ok, OkWriter<TRequest, TOk>.Write);

    /// <summary>A failure with a status other than <see cref="Status.Ok"/> and the <see cref="Error"/> that explains it.</summary>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="status">Why it failed.</param>
    /// <param name="error">The explanation.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentException"><paramref name="status"/> is <see cref="Status.Ok"/>.</exception>
    public static Response Failure(uint correlation, Status status, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (status == Status.Ok)
            throw new ArgumentException("A failure needs a status other than Ok; use Response.Ok for a success.", nameof(status));
        return new Response(correlation, status, default, error, WriteError);
    }

    /// <summary>The answer to a request id the Host does not know (§1.5): status <c>UnknownRequest</c>.</summary>
    /// <param name="correlation">The request's correlation.</param>
    /// <returns>The response.</returns>
    public static Response ForUnknownRequest(uint correlation) =>
        Failure(correlation, Status.UnknownRequest, new Error { Message = "This Host does not know the request." });

    /// <summary>Reads the OK body as the one of <typeparamref name="TRequest"/>, exactly to its end.</summary>
    /// <typeparam name="TRequest">The request this answers.</typeparam>
    /// <typeparam name="TOk">Its OK body.</typeparam>
    /// <param name="ok">The OK body.</param>
    /// <returns><see langword="false"/> when the status is not <c>Ok</c> or the body is not that layout.</returns>
    public bool TryReadOk<TRequest, TOk>([MaybeNullWhen(false)] out TOk ok)
        where TRequest : LocalRequest, IRequest<TRequest, TOk>
    {
        ok = default;
        if (!IsOk)
            return false;
        if (writeValue is not null)
        {
            if (value is not TOk built)
                return false;
            ok = built;
            return true;
        }

        var reader = new WireReader(Body);
        return TRequest.TryReadOk(ref reader, out ok) && reader.IsAtEnd;
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

    /// <inheritdoc />
    public override string ToString() => $"#{Correlation} {Status}";

    internal static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Response? message)
    {
        uint correlation = reader.UInt32BigEndian();
        var status = (Status)reader.UInt16BigEndian();
        ReadOnlyMemory<byte> body = reader.RestView();
        message = reader.Ok && correlation != 0 ? new Response(correlation, status, body, null, null) : null;
        return message is not null;
    }

    internal override void WritePayload(ref WireWriter writer)
    {
        writer.UInt32BigEndian(Correlation);
        writer.UInt16BigEndian((ushort)Status);
        if (writeValue is not null)
            writeValue(value!, ref writer);
        else
            writer.Bytes(Body.Span);
    }

    private static void WriteError(object error, ref WireWriter writer) => ((Error)error).Write(ref writer);
}
