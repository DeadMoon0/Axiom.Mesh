using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Chat;

/// <summary>
/// Cuts a page of a timeline (CHAT-SERVICE.md §5.3): the messages after the previous page's last one, in the page's
/// order, until the limit, <see cref="ChatLimits.TimelinePage"/> or the byte budget of one reply is reached, whichever
/// comes first; <c>More</c> says something was left. Pure.
/// </summary>
internal static class TimelinePage
{
    /// <summary>The bytes of a reply besides its messages: the list count (<c>u16</c>) and <c>more</c> (<c>bool</c>).</summary>
    public const int Overhead = 3;

    /// <summary>Cuts a page.</summary>
    /// <param name="candidates">
    /// The messages in the page's order, starting at or before the first one after <paramref name="after"/>; one more than
    /// the limit, when there are, so that <c>More</c> is known.
    /// </param>
    /// <param name="after">The previous page's last key; <see langword="null"/> for the first page.</param>
    /// <param name="reverse">Newest first.</param>
    /// <param name="limit">At most this many, 1 to <see cref="ChatLimits.TimelinePage"/>.</param>
    /// <param name="byteBudget">The most bytes the reply's encoding may take.</param>
    /// <returns>The page.</returns>
    public static TimelineReply Cut(IReadOnlyList<MessageView> candidates, EntryKey? after, bool reverse, int limit, int byteBudget)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        int most = Math.Clamp(limit, 1, ChatLimits.TimelinePage);
        var page = new List<MessageView>(Math.Min(most, candidates.Count));
        int bytes = Overhead;
        foreach (MessageView candidate in candidates)
        {
            if (!Timeline.Later(candidate.Key, after, reverse))
                continue;
            if (page.Count == most)
                return new TimelineReply { Messages = page, More = true };
            int size = ChatCodec.SizeOf(candidate);
            if (bytes + size > byteBudget)
                return new TimelineReply { Messages = page, More = true };
            page.Add(candidate);
            bytes += size;
        }

        return new TimelineReply { Messages = page, More = false };
    }
}
