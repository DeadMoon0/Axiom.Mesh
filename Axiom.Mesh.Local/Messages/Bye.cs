using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// <c>Bye</c> (3, LOCAL-SPEC.md §4.3): either side ends the connection and closes. Accepted in every state; a receiver
/// sends nothing further.
/// </summary>
public sealed class Bye : LocalMessage, IClientMessage, IHostMessage
{
    /// <summary>Why; a value this side does not know is read as <see cref="ByeReason.Unspecified"/>.</summary>
    public ByeReason Reason { get; init; }

    /// <summary>For logs; never interpreted. At most 256 UTF-8 bytes.</summary>
    public string Detail { get; init; } = "";

    /// <inheritdoc />
    public override ushort MessageId => LocalMessageIds.Bye;

    /// <summary>A <c>Bye</c> whose detail is cut to its 256 bytes, never in the middle of a character.</summary>
    /// <param name="reason">Why.</param>
    /// <param name="detail">Any text, for logs.</param>
    /// <returns>The message.</returns>
    public static Bye Of(ByeReason reason, string detail) => new() { Reason = reason, Detail = Truncate(detail ?? "") };

    /// <inheritdoc />
    public override string ToString() => Detail.Length == 0 ? $"Bye({Reason})" : $"Bye({Reason}, \"{Detail}\")";

    internal static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Bye? message)
    {
        byte reason = reader.UInt8();
        var read = new Bye
        {
            Reason = reason <= (byte)ByeReason.Done ? (ByeReason)reason : ByeReason.Unspecified,
            Detail = Wire.ReadString(ref reader, FieldBounds.ByeDetailLength),
        };
        message = reader.Ok ? read : null;
        return message is not null;
    }

    internal override void WritePayload(ref WireWriter writer)
    {
        writer.UInt8((byte)Reason);
        Wire.WriteString(ref writer, Detail, FieldBounds.ByeDetailLength);
    }

    private static string Truncate(string detail)
    {
        string clean = detail.Replace('\0', ' ');
        if (Encoding.UTF8.GetByteCount(clean) <= FieldBounds.ByeDetailLength)
            return clean;

        int length = Math.Min(clean.Length, FieldBounds.ByeDetailLength);
        while (length > 0 && (Encoding.UTF8.GetByteCount(clean.AsSpan(0, length)) > FieldBounds.ByeDetailLength || char.IsHighSurrogate(clean[length - 1])))
            length--;
        return clean[..length];
    }
}
