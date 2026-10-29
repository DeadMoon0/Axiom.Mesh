using System;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// A frame of the connection's control lane (a <c>Response</c>, <c>Ping</c>, <c>Drain</c> or <c>Bye</c>) with its encoded
/// size and what to do once it was written: a <c>Subscribe</c>'s events may follow, an operation's events may start.
/// </summary>
internal sealed class OutFrame
{
    /// <summary>A frame.</summary>
    /// <param name="message">The message.</param>
    /// <param name="size">Its encoded size.</param>
    /// <param name="written">Called once the frame was written; <see langword="null"/> for nothing.</param>
    public OutFrame(IHostMessage message, long size, Action? written)
    {
        Message = message;
        Size = size;
        Written = written;
    }

    /// <summary>The message.</summary>
    public IHostMessage Message { get; }

    /// <summary>Its encoded size.</summary>
    public long Size { get; }

    /// <summary>Called once the frame was written.</summary>
    public Action? Written { get; }

    /// <summary>A session frame (Ping, Pong, Drain, Bye).</summary>
    /// <param name="message">The message.</param>
    /// <returns>The frame.</returns>
    public static OutFrame Session(IHostMessage message) => new(message, FrameSizes.OfSession(), null);

    /// <inheritdoc />
    public override string ToString() => $"{Message} ({Size} B)";
}
