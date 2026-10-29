using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>The checks every object parser ends with, and the faults they name.</summary>
internal static class ObjectReading
{
    /// <summary>The reader failed or bytes are left: malformed either way (SPEC.md §8.1).</summary>
    /// <param name="reader">The reader, after the last field.</param>
    /// <param name="what">The object, for the detail.</param>
    /// <param name="fault">The fault, when there is one.</param>
    /// <returns><see langword="true"/> when the body parsed exactly to its end.</returns>
    public static bool AtEnd(ref WireReader reader, string what, out ObjectFault fault)
    {
        if (!reader.Ok)
        {
            fault = ObjectFault.Malformed($"A field of the {what} is outside its bounds or the body ends early (SPEC.md §5.2, §8.1).");
            return false;
        }

        if (!reader.IsAtEnd)
        {
            fault = ObjectFault.Malformed($"Bytes follow the last field of the {what} (SPEC.md §8.1).");
            return false;
        }

        fault = default;
        return true;
    }

    /// <summary>
    /// Reads the <c>signature</c> a signed object ends with and checks the body ended there: a signature outside the
    /// bounds of SPEC.md §2.2 makes the object malformed (§8.1).
    /// </summary>
    /// <param name="reader">The reader, before the signature.</param>
    /// <param name="what">The object, for the detail.</param>
    /// <param name="signature">The signature read.</param>
    /// <param name="fault">The fault, when there is one.</param>
    /// <returns><see langword="true"/> when a low-s signature ended the body.</returns>
    public static bool SignatureAtEnd(ref WireReader reader, string what, out Signature64 signature, out ObjectFault fault)
    {
        signature = default;
        ReadOnlyMemory<byte> bytes = reader.BytesView(Signature64.Length);
        if (!AtEnd(ref reader, what, out fault))
            return false;
        if (Signature64.TryParse(bytes.Span, out signature))
            return true;

        return Fail($"The {what}'s signature is outside 1 ≤ r < n, 1 ≤ s ≤ (n − 1)/2 (SPEC.md §2.2).", out fault);
    }

    /// <summary>A fault for a rule the bytes break.</summary>
    /// <param name="detail">The rule, citing its section.</param>
    /// <param name="fault">The fault.</param>
    /// <returns><see langword="false"/>, for <c>return Fail(...)</c>.</returns>
    public static bool Fail(string detail, out ObjectFault fault)
    {
        fault = ObjectFault.Malformed(detail);
        return false;
    }
}
