using System;
using System.Text;

namespace Axiom.Mesh.Sync;

/// <summary>A text cut to a number of UTF-8 bytes between characters, for the bounded strings of the namespace. Pure.</summary>
internal static class TextCut
{
    /// <summary>The text, or its longest start that fits.</summary>
    /// <param name="text">The text; <see langword="null"/> reads as empty.</param>
    /// <param name="maxBytes">The most UTF-8 bytes.</param>
    /// <returns>The text cut to fit, NUL replaced by a space.</returns>
    public static string Of(string? text, int maxBytes)
    {
        string clean = (text ?? string.Empty).Replace('\0', ' ');
        if (Encoding.UTF8.GetByteCount(clean) <= maxBytes)
            return clean;
        int length = clean.Length;
        while (length > 0 && Encoding.UTF8.GetByteCount(clean.AsSpan(0, length)) > maxBytes)
            length--;
        if (length > 0 && char.IsHighSurrogate(clean[length - 1]))
            length--;
        return clean[..length];
    }
}
