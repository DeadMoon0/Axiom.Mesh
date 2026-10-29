namespace Axiom.Mesh.Local;

/// <summary>The class of a frame, known from its message id alone (LOCAL-SPEC.md §3.3).</summary>
public enum MessageClass : byte
{
    /// <summary>0, and 2048 to 65 535: never assigned; a violation when received.</summary>
    Unassigned = 0,

    /// <summary>1 to 31, sent by both sides, no envelope.</summary>
    Session = 1,

    /// <summary>32, sent by the Host: <c>correlation status body</c>.</summary>
    Response = 2,

    /// <summary>33, sent by a service: <c>callback status body</c>.</summary>
    Reply = 3,

    /// <summary>34 to 255: reserved; a violation when received.</summary>
    Reserved = 4,

    /// <summary>256 to 1023, sent by a client: <c>correlation body</c>.</summary>
    Request = 5,

    /// <summary>1024 to 1535, sent by the Host: <c>subscription body</c>.</summary>
    Event = 6,

    /// <summary>1536 to 2047, sent by the Host to a service: <c>callback body</c>.</summary>
    Callback = 7,
}
