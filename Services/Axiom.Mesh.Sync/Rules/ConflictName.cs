using System;
using System.Collections.Generic;
using System.Text;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The name the other side's copy of a conflicting path is kept under (SPEC.md §9.5):
/// <c>&lt;name&gt;.conflict-&lt;first 8 hex of the other deviceId&gt;</c> before any extension, and <c>-2</c>, <c>-3</c>, …
/// appended when that name is taken. The extension starts at the first dot that is not the first character, so
/// <c>report.txt</c> becomes <c>report.conflict-ab12cd34.txt</c>, <c>archive.tar.gz</c> becomes
/// <c>archive.conflict-ab12cd34.tar.gz</c>, and <c>Makefile</c> or <c>.bashrc</c> get the tag at the end. The result is at most
/// 255 UTF-8 bytes (a Tree name, SPEC.md §8.4): the stem is shortened first. Names are compared case-insensitively whatever the
/// platform, so every node chooses the same name. Pure.
/// </summary>
internal static class ConflictName
{
    private const string Tag = ".conflict-";

    /// <summary>The copy's name.</summary>
    /// <param name="name">The conflicting entry's name.</param>
    /// <param name="other">The device whose leaf lost.</param>
    /// <param name="taken">The names already in the directory, the copies chosen before included; compared case-insensitively.</param>
    /// <returns>A name not in <paramref name="taken"/>.</returns>
    public static string Of(string name, DeviceId other, IReadOnlySet<string> taken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(taken);
        int dot = name.IndexOf('.', 1);
        string stem = dot < 0 ? name : name[..dot];
        string extension = dot < 0 ? string.Empty : name[dot..];
        string tag = Tag + other.Value.ToHex()[..8];
        for (int n = 1; ; n++)
        {
            string suffix = n == 1 ? tag : $"{tag}-{n}";
            string candidate = Fit(stem, suffix, extension);
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    // stem + suffix + extension in at most 255 UTF-8 bytes: the stem shortened first, then the extension.
    private static string Fit(string stem, string suffix, string extension)
    {
        while (Encoding.UTF8.GetByteCount(stem) + Encoding.UTF8.GetByteCount(suffix) + Encoding.UTF8.GetByteCount(extension) > Limits.MaxTreeNameLength)
        {
            if (stem.Length > 1)
                stem = Shorter(stem);
            else
                extension = extension.Length > 0 ? Shorter(extension) : extension;
        }

        return stem + suffix + extension;
    }

    // One character less, never splitting a surrogate pair.
    private static string Shorter(string text) =>
        text.Length >= 2 && char.IsLowSurrogate(text[^1]) && char.IsHighSurrogate(text[^2]) ? text[..^2] : text[..^1];
}
