using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Chat;

/// <summary>
/// Threads of a Channel (CHAT-SERVICE.md §5.5, PAYLOADS.md §3 <c>replyTo</c>): a message's root is found by following
/// <c>replyTo</c> up through messages that are indexed, at most <see cref="ChatLimits.ThreadDepth"/> steps; the replies of
/// a root are every message whose chain leads to it, at any depth. A withdrawn message stays in its place, so a reply
/// survives its root's withdrawal. Pure.
/// </summary>
internal static class Threading
{
    /// <summary>The root of a message's thread.</summary>
    /// <param name="message">The message.</param>
    /// <param name="replyToOf">Every indexed message of the Channel with the message it answers, zero for none.</param>
    /// <param name="maxDepth">The most steps up.</param>
    /// <returns>The topmost indexed message of the chain; the message itself when it answers nothing indexed.</returns>
    public static ObjectHash RootOf(ObjectHash message, IReadOnlyDictionary<ObjectHash, ObjectHash> replyToOf, int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(replyToOf);
        ObjectHash current = message;
        var seen = new HashSet<ObjectHash> { current };
        for (int step = 0; step < maxDepth; step++)
        {
            if (!replyToOf.TryGetValue(current, out ObjectHash parent) || parent.IsZero || !replyToOf.ContainsKey(parent) || !seen.Add(parent))
                break;
            current = parent;
        }

        return current;
    }

    /// <summary>Every message under a root, at any depth, the root excluded.</summary>
    /// <param name="root">The root.</param>
    /// <param name="replyToOf">Every indexed message of the Channel with the message it answers, zero for none.</param>
    /// <returns>The replies, unordered.</returns>
    public static IReadOnlySet<ObjectHash> Replies(ObjectHash root, IReadOnlyDictionary<ObjectHash, ObjectHash> replyToOf)
    {
        ArgumentNullException.ThrowIfNull(replyToOf);
        ILookup<ObjectHash, ObjectHash> children = replyToOf.Where(p => !p.Value.IsZero).ToLookup(p => p.Value, p => p.Key);
        var replies = new HashSet<ObjectHash>();
        var pending = new Queue<ObjectHash>([root]);
        while (pending.TryDequeue(out ObjectHash parent))
        {
            foreach (ObjectHash child in children[parent])
            {
                if (child != root && replies.Add(child))
                    pending.Enqueue(child);
            }
        }

        return replies;
    }
}
