using System;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>PairCancel</c> (3, PAIR-SPEC.md §5.4): the pairing ends without success; the receiver sends nothing more.
/// </summary>
public sealed class PairCancel : PairMessage, IWireMessage<PairCancel>
{
    /// <summary>An end of the pairing.</summary>
    /// <param name="reason">Why; a value <see cref="PairCancelReason"/> does not name is sent as <see cref="PairCancelReason.Unspecified"/>.</param>
    /// <param name="detail">Text for logs and the user, at most 256 UTF-8 bytes (see <see cref="Bye.Shorten"/>).</param>
    /// <exception cref="ArgumentException">The detail is too long, holds a NUL or an unpaired surrogate.</exception>
    public PairCancel(PairCancelReason reason, string detail)
    {
        Wire.StringLength(detail, Limits.MaxByeDetailLength, nameof(detail));
        Reason = Enum.IsDefined(reason) ? reason : PairCancelReason.Unspecified;
        Detail = detail;
    }

    /// <summary>Why the pairing ends.</summary>
    public PairCancelReason Reason { get; }

    /// <summary>Text for logs and the user; must not be interpreted.</summary>
    public string Detail { get; }

    /// <summary>
    /// The <c>PairCancel</c> a pairing session sends when the peer broke the protocol: <see cref="PairCancelReason.Expired"/>
    /// when nothing arrived in time, <see cref="PairCancelReason.Violation"/> otherwise.
    /// </summary>
    /// <param name="violation">What ProtoStream detected.</param>
    /// <returns>The closing message.</returns>
    public static PairCancel ForViolation(Violation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);
        PairCancelReason reason = violation.Code == ViolationCode.Timeout ? PairCancelReason.Expired : PairCancelReason.Violation;
        return new PairCancel(reason, Bye.Shorten(violation.Detail));
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out PairCancel message)
    {
        var reason = (PairCancelReason)reader.UInt8();
        string detail = Wire.ReadString(ref reader, Limits.MaxByeDetailLength);
        message = new PairCancel(reason, reader.Ok ? detail : string.Empty);
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt8((byte)Reason);
        Wire.WriteString(ref writer, Detail, Limits.MaxByeDetailLength);
    }

    /// <inheritdoc />
    public override string ToString() => Detail.Length == 0 ? $"PairCancel {Reason}" : $"PairCancel {Reason}: {Detail}";
}
