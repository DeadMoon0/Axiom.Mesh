using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Core;

/// <summary>
/// Cuts a list answer to its bounds (LOCAL-SPEC.md §6 header, §3.1): the first elements in their stated order, at most the
/// list's maximum and no more bytes than fit one frame; <c>more</c> says that an element was left out.
/// </summary>
internal static class PageCut
{
    /// <summary>What one frame holds for a list's elements: the frame limit less the header, the envelope and the list's own fields.</summary>
    public const long FrameBudget = Limits.MaxFrameLength - 64;

    /// <summary>Takes elements in order while the count and the bytes stay within their bounds.</summary>
    /// <typeparam name="T">The element.</typeparam>
    /// <param name="ordered">The elements, in the list's order; read one past the page at most.</param>
    /// <param name="max">The most elements a page holds.</param>
    /// <param name="budget">The most bytes the elements may take.</param>
    /// <param name="size">An element's encoded size.</param>
    /// <returns>The page, and whether an element was left out.</returns>
    public static (List<T> Page, bool More) Take<T>(IEnumerable<T> ordered, int max, long budget, Func<T, long> size)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(size);
        var page = new List<T>(Math.Min(max, 64));
        long used = 0;
        foreach (T element in ordered)
        {
            long bytes = size(element);
            if (page.Count == max || used + bytes > budget)
                return (page, true);
            page.Add(element);
            used += bytes;
        }

        return (page, false);
    }
}
