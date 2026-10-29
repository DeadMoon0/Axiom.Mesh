namespace Axiom.Mesh.Protocol;

/// <summary>Why a <c>ServiceOpen</c> was refused (SPEC.md §7.10). A value this enumeration does not name is read as <see cref="Unspecified"/>.</summary>
public enum ServiceRefusal : byte
{
    /// <summary>No reason given, or one this node does not know.</summary>
    Unspecified = 0,

    /// <summary>The service is unknown to the receiver.</summary>
    Unknown = 1,

    /// <summary>The receiver is busy, or withdrew its own <c>ServiceOpen</c> by the tie rule.</summary>
    Busy = 2,

    /// <summary>The peer may not open the service.</summary>
    NotAllowed = 3,
}
