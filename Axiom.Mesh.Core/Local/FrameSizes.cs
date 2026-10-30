using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Core;

/// <summary>
/// The encoded size of the frames the local port queues (LOCAL-SPEC.md §3.1, §7.4): the 4-byte length, the 2-byte id,
/// the envelope and the body, counted once when the frame is made, so that the bounds in events and in bytes hold
/// without holding encoded bytes twice.
/// </summary>
internal static class FrameSizes
{
    private const int Header = 4 + 2;
    private const int EventEnvelope = 4;
    private const int ResponseEnvelope = 4 + 2;

    /// <summary>The frame of an event on a subscription.</summary>
    /// <param name="event">The event.</param>
    /// <returns>Its size in bytes.</returns>
    public static long OfEvent(LocalEvent @event)
    {
        using var counter = new CountingBufferWriter();
        var writer = new WireWriter(counter);
        @event.Write(ref writer);
        return Header + EventEnvelope + counter.Count;
    }

    /// <summary>The frame of a successful response.</summary>
    /// <typeparam name="TRequest">The request answered.</typeparam>
    /// <typeparam name="TOk">Its OK body.</typeparam>
    /// <param name="ok">The OK body.</param>
    /// <returns>Its size in bytes.</returns>
    public static long OfOk<TRequest, TOk>(TOk ok)
        where TRequest : LocalRequest, IRequest<TRequest, TOk>
    {
        using var counter = new CountingBufferWriter();
        var writer = new WireWriter(counter);
        TRequest.WriteOk(ref writer, ok);
        return Header + ResponseEnvelope + counter.Count;
    }

    /// <summary>The frame of a callback to a service (§3.6).</summary>
    /// <param name="call">The callback's body.</param>
    /// <returns>Its size in bytes.</returns>
    public static long OfCallback(LocalCallback call)
    {
        using var counter = new CountingBufferWriter();
        var writer = new WireWriter(counter);
        call.Write(ref writer);
        return Header + EventEnvelope + counter.Count;
    }

    /// <summary>The frame of a failed response.</summary>
    /// <param name="error">Its error.</param>
    /// <returns>Its size in bytes.</returns>
    public static long OfError(Error error)
    {
        using var counter = new CountingBufferWriter();
        var writer = new WireWriter(counter);
        error.Write(ref writer);
        return Header + ResponseEnvelope + counter.Count;
    }

    /// <summary>The encoded size of one record inside a list (LOCAL-SPEC.md §5), for cutting a page to the frame.</summary>
    /// <typeparam name="T">The record's type.</typeparam>
    /// <param name="record">The record.</param>
    /// <returns>Its size in bytes.</returns>
    public static long OfRecord<T>(T record)
        where T : class, IWireRecord<T>
    {
        using var counter = new CountingBufferWriter();
        var writer = new WireWriter(counter);
        record.Write(ref writer);
        return counter.Count;
    }

    /// <summary>A session frame (Ping, Pong, Drain, Bye): small and counted as at most its largest form.</summary>
    /// <returns>An upper bound of its size.</returns>
    public static long OfSession() => Header + 1 + 2 + 256;
}
