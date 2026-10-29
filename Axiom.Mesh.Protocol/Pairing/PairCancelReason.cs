namespace Axiom.Mesh.Protocol;

/// <summary>Why a pairing ends without success (PAIR-SPEC.md §5.4, §11.2). A value this enumeration does not name is read as <see cref="Unspecified"/>.</summary>
public enum PairCancelReason : byte
{
    /// <summary>No reason given, or one this side does not know.</summary>
    Unspecified = 0,

    /// <summary>A user cancelled.</summary>
    Cancelled = 1,

    /// <summary>The other side runs another pairing.</summary>
    Busy = 2,

    /// <summary>The other side's <c>PairHello</c> or enrolment Roster failed its checks.</summary>
    Refused = 3,

    /// <summary>The pairing outlived its 10 minutes, or the other side sent nothing in time.</summary>
    Expired = 4,

    /// <summary>The other side broke the pairing protocol.</summary>
    Violation = 5,
}
