using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// A request on the wire (LOCAL-SPEC.md §3.4): the client's correlation and the request's body. A client builds one per
/// request (<c>LocalClient</c> does it for you); the Host's codec reads the body through <see cref="RequestTable"/>, so
/// <see cref="Request"/> is the typed request, and answers it with exactly one <see cref="Response"/> of the same
/// correlation.
/// </summary>
public sealed class RequestFrame : LocalMessage, IClientMessage
{
    /// <summary>Puts a request in a frame.</summary>
    /// <param name="correlation">The correlation, never 0, not outstanding on the connection.</param>
    /// <param name="request">The request.</param>
    /// <exception cref="ArgumentException"><paramref name="correlation"/> is 0.</exception>
    public RequestFrame(uint correlation, LocalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (correlation == 0)
            throw new ArgumentException("A correlation is never 0 (LOCAL-SPEC.md §3.2).", nameof(correlation));
        Correlation = correlation;
        Request = request;
    }

    /// <summary>The correlation the <see cref="Response"/> carries back.</summary>
    public uint Correlation { get; }

    /// <summary>The request.</summary>
    public LocalRequest Request { get; }

    /// <inheritdoc />
    public override ushort MessageId => Request.MessageId;

    /// <inheritdoc />
    public override string ToString() => $"#{Correlation} {Request}";

    internal override void WritePayload(ref WireWriter writer)
    {
        writer.UInt32BigEndian(Correlation);
        Request.Write(ref writer);
    }
}
