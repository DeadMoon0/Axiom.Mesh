namespace Axiom.Mesh.Local;

/// <summary>The message ids of the session, response and reply frames and the ranges of the classes (LOCAL-SPEC.md §3.3, §14.1).</summary>
public static class LocalMessageIds
{
    /// <summary><c>Hello</c>, sent by a client first (§4.2).</summary>
    public const ushort Hello = 1;

    /// <summary><c>HelloAck</c>, the Host's answer to <c>Hello</c> (§4.2).</summary>
    public const ushort HelloAck = 2;

    /// <summary><c>Bye</c>, either side (§4.3).</summary>
    public const ushort Bye = 3;

    /// <summary><c>Ping</c>, either side (§4.4).</summary>
    public const ushort Ping = 4;

    /// <summary><c>Pong</c>, either side (§4.4).</summary>
    public const ushort Pong = 5;

    /// <summary><c>Drain</c>, the Host (§4.5).</summary>
    public const ushort Drain = 6;

    /// <summary>The last id of the session range; 7 to 31 are session extensions, a violation when unknown.</summary>
    public const ushort LastSession = 31;

    /// <summary><c>Response</c>, the Host (§3.4).</summary>
    public const ushort Response = 32;

    /// <summary><c>Reply</c>, a service (§3.6).</summary>
    public const ushort Reply = 33;

    /// <summary>The first id of the request range.</summary>
    public const ushort FirstRequest = 256;

    /// <summary>The first id of the service requests within the request range.</summary>
    public const ushort FirstServiceRequest = 512;

    /// <summary>The first id of the request extensions of either role within the request range.</summary>
    public const ushort FirstRequestExtension = 768;

    /// <summary>The last id of the request range.</summary>
    public const ushort LastRequest = 1023;

    /// <summary>The first id of the event range.</summary>
    public const ushort FirstEvent = 1024;

    /// <summary>The last id of the event range.</summary>
    public const ushort LastEvent = 1535;

    /// <summary>The first id of the callback range (1536 itself is reserved).</summary>
    public const ushort FirstCallback = 1536;

    /// <summary>The last id of the callback range; 2048 and above are never assigned.</summary>
    public const ushort LastCallback = 2047;

    /// <summary>The class of a frame with message id <paramref name="id"/> (§3.3).</summary>
    /// <param name="id">A message id.</param>
    /// <returns>Its class.</returns>
    public static MessageClass ClassOf(ushort id) => id switch
    {
        0 => MessageClass.Unassigned,
        <= LastSession => MessageClass.Session,
        Response => MessageClass.Response,
        Reply => MessageClass.Reply,
        < FirstRequest => MessageClass.Reserved,
        <= LastRequest => MessageClass.Request,
        <= LastEvent => MessageClass.Event,
        <= LastCallback => MessageClass.Callback,
        _ => MessageClass.Unassigned,
    };
}
