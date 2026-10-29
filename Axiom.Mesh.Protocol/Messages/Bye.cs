using System;
using System.Text;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>Bye</c> (15, SPEC.md §7.13): the orderly close, accepted in Connected and Established. A node that receives it sends
/// nothing more (§6.4). A malformed <c>Bye</c> is treated as a close (§6.1): <see cref="Amp.Definition"/> decodes it as a
/// <c>Bye</c> with <see cref="ByeReason.Unspecified"/> and no detail, never as a violation.
/// </summary>
public sealed class Bye : MeshMessage, IWireMessage<Bye>
{
    /// <summary>A close.</summary>
    /// <param name="reason">Why; a value <see cref="ByeReason"/> does not name is sent as <see cref="ByeReason.Unspecified"/>.</param>
    /// <param name="detail">Text for logs, never interpreted: at most 256 UTF-8 bytes (see <see cref="Shorten"/>).</param>
    /// <exception cref="ArgumentException">The detail is too long, holds a NUL or an unpaired surrogate.</exception>
    public Bye(ByeReason reason, string detail)
    {
        Wire.StringLength(detail, Limits.MaxByeDetailLength, nameof(detail));
        Reason = Enum.IsDefined(reason) ? reason : ByeReason.Unspecified;
        Detail = detail;
    }

    /// <inheritdoc />
    public override ushort Id => MessageIds.Bye;

    /// <summary>Why the session closes.</summary>
    public ByeReason Reason { get; }

    /// <summary>Text for logs; must not be interpreted.</summary>
    public string Detail { get; }

    /// <summary>
    /// The <c>Bye</c> a session sends when the peer broke the protocol (§12.1): <see cref="ByeReason.Idle"/> when nothing
    /// arrived in time (§6.1, §6.3), <see cref="ByeReason.Violation"/> otherwise, with ProtoStream's detail shortened.
    /// </summary>
    /// <param name="violation">What ProtoStream detected.</param>
    /// <returns>The closing message.</returns>
    public static Bye ForViolation(Violation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);
        ByeReason reason = violation.Code == ViolationCode.Timeout ? ByeReason.Idle : ByeReason.Violation;
        return new Bye(reason, Shorten(violation.Detail));
    }

    /// <summary>
    /// Cuts a text to what a <c>detail</c> may hold: at most 256 UTF-8 bytes, without NUL or unpaired surrogates, never
    /// splitting a character.
    /// </summary>
    /// <param name="text">Any text.</param>
    /// <returns>The longest prefix that fits, with NUL and unpaired surrogates replaced.</returns>
    public static string Shorten(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(Math.Min(text.Length, Limits.MaxByeDetailLength));
        int bytes = 0;
        for (int i = 0; i < text.Length; i++)
        {
            bool pair = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
            string unit = pair ? text.Substring(i, 2)
                : text[i] == '\0' || char.IsSurrogate(text[i]) ? "?"
                : text[i].ToString();
            int size = Encoding.UTF8.GetByteCount(unit);
            if (bytes + size > Limits.MaxByeDetailLength)
                break;
            builder.Append(unit);
            bytes += size;
            if (pair)
                i++;
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out Bye message)
    {
        var reason = (ByeReason)reader.UInt8();
        string detail = Wire.ReadString(ref reader, Limits.MaxByeDetailLength);
        message = reader.Ok ? new Bye(reason, detail) : Unreadable;
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt8((byte)Reason);
        Wire.WriteString(ref writer, Detail, Limits.MaxByeDetailLength);
    }

    /// <inheritdoc />
    public override string ToString() => Detail.Length == 0 ? $"Bye {Reason}" : $"Bye {Reason}: {Detail}";

    /// <summary>What a malformed <c>Bye</c> is read as: a close for no stated reason (§6.1).</summary>
    internal static Bye Unreadable => new(ByeReason.Unspecified, string.Empty);
}
