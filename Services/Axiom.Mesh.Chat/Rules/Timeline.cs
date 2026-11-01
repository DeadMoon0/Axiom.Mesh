using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Axiom.Mesh.Chat;

/// <summary>
/// How a Channel's messages line up and what of each is shown (SPEC.md §9.3, §9.4; PAYLOADS.md §1.2): in the total order
/// of their Entry keys, identically on every node that holds them; a withdrawn or broken message is placed but never shows
/// its text or attachments. Pure.
/// </summary>
internal static class Timeline
{
    /// <summary>Items in the order a timeline shows them: ascending by key, or descending.</summary>
    /// <typeparam name="T">The item.</typeparam>
    /// <param name="items">The items.</param>
    /// <param name="key">An item's position in the total order.</param>
    /// <param name="reverse">Newest first.</param>
    /// <returns>The items in order.</returns>
    public static IEnumerable<T> Order<T>(IEnumerable<T> items, Func<T, EntryKey> key, bool reverse) =>
        reverse ? items.OrderByDescending(key) : items.OrderBy(key);

    /// <summary>Whether a message lies beyond the previous page's last one in the page's direction.</summary>
    /// <param name="key">The message's key.</param>
    /// <param name="after">The previous page's last key; <see langword="null"/> for the first page.</param>
    /// <param name="reverse">Newest first.</param>
    /// <returns><see langword="true"/> when it belongs on this page or a later one.</returns>
    public static bool Later(EntryKey key, EntryKey? after, bool reverse) =>
        after is not { } previous || (reverse ? key < previous : key > previous);

    /// <summary>Whether a message's text and attachments may be shown: neither withdrawn (SPEC.md §9.4) nor broken (PAYLOADS.md §1.2).</summary>
    /// <param name="withdrawn">An honoured Redaction names it.</param>
    /// <param name="broken">Its payload or envelope is the writer's fault.</param>
    /// <returns>Whether its content is shown.</returns>
    public static bool Visible(bool withdrawn, bool broken) => !withdrawn && !broken;

    /// <summary>The start of a text that fits <paramref name="maxBytes"/> UTF-8 bytes, cut between characters, never inside one.</summary>
    /// <param name="text">The text.</param>
    /// <param name="maxBytes">The most bytes.</param>
    /// <returns>The preview.</returns>
    public static string Preview(string text, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
            return text;
        int bytes = 0;
        int end = 0;
        while (end < text.Length)
        {
            int width = char.IsHighSurrogate(text[end]) && end + 1 < text.Length ? 2 : 1;
            int size = Encoding.UTF8.GetByteCount(text.AsSpan(end, width));
            if (bytes + size > maxBytes)
                break;
            bytes += size;
            end += width;
        }

        return text[..end];
    }
}
