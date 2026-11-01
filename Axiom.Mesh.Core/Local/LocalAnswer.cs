using System;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The one <c>Response</c> to a request (LOCAL-SPEC.md §3.4) with its encoded size, and what may follow once it is
/// written: a subscription's first event, an operation's first event.
/// </summary>
internal sealed class LocalAnswer
{
    private LocalAnswer(Response response, long size, Status status, Action? written)
    {
        Response = response;
        Size = size;
        Status = status;
        Written = written;
    }

    /// <summary>The response.</summary>
    public Response Response { get; }

    /// <summary>Its encoded size.</summary>
    public long Size { get; }

    /// <summary>Its status.</summary>
    public Status Status { get; }

    /// <summary>Called once the response was written; <see langword="null"/> for nothing.</summary>
    public Action? Written { get; }

    /// <summary>A success.</summary>
    /// <typeparam name="TRequest">The request answered.</typeparam>
    /// <typeparam name="TOk">Its OK body.</typeparam>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="ok">The OK body.</param>
    /// <returns>The answer.</returns>
    public static LocalAnswer Ok<TRequest, TOk>(uint correlation, TOk ok)
        where TRequest : LocalRequest, IRequest<TRequest, TOk> =>
        new(Response.Ok<TRequest, TOk>(correlation, ok), FrameSizes.OfOk<TRequest, TOk>(ok), Status.Ok, null);

    /// <summary>A failure with its explanation.</summary>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="status">Why, never Ok.</param>
    /// <param name="message">For display and logs, one line.</param>
    /// <param name="detail">For logs; the field that is wrong for <c>Invalid</c>.</param>
    /// <returns>The answer.</returns>
    public static LocalAnswer Failure(uint correlation, Status status, string message, string detail)
    {
        var error = new Error { Message = Cut(message, 256), Detail = Cut(detail, 1024) };
        return new LocalAnswer(Response.Failure(correlation, status, error), FrameSizes.OfError(error), status, null);
    }

    /// <summary>A failure with an explanation somebody else wrote: a service's <c>Error</c>, passed on unchanged (§6.7, §9.8).</summary>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="status">Why, never Ok.</param>
    /// <param name="error">The explanation, already within its bounds (§5.14).</param>
    /// <returns>The answer.</returns>
    public static LocalAnswer Failure(uint correlation, Status status, Error error) =>
        new(Response.Failure(correlation, status, error), FrameSizes.OfError(error), status, null);

    /// <summary>The same answer, with something to do once it was written.</summary>
    /// <param name="written">What to do.</param>
    /// <returns>The answer.</returns>
    public LocalAnswer Then(Action written) => new(Response, Size, Status, written);

    /// <summary>The control frame that carries it.</summary>
    /// <returns>The frame.</returns>
    public OutFrame Frame() => new(Response, Size, Written);

    /// <inheritdoc />
    public override string ToString() => Response.ToString();

    // An Error's fields are bounded in bytes (§5.14); a message is cut on a character boundary, well inside the bound.
    private static string Cut(string text, int maxBytes)
    {
        text ??= string.Empty;
        int limit = maxBytes / 4;
        return text.Length <= limit ? text : text[..limit];
    }
}
