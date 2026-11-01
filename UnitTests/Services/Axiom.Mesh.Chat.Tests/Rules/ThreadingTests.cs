using System.Collections.Generic;
using Axiom.Mesh.Chat.Tests.Support;

namespace Axiom.Mesh.Chat.Tests.Rules;

/// <summary>
/// Threads (CHAT-SERVICE.md §5.5): a message's root is the topmost indexed message up its <c>replyTo</c> chain, and the
/// replies of a root are every message whose chain reaches it, at any depth; a cycle or an unindexed parent ends the walk.
/// </summary>
public sealed class ThreadingTests
{
    // §5.5: up the chain to the topmost indexed message (a parent not indexed is not followed), and down to every reply at
    // any depth; a cycle (which honest writers cannot make) ends the walk instead of looping.
    [Fact]
    public void TheRootIsTheTopmostIndexedMessageUpTheChain_AndItsRepliesAreEveryMessageBelowIt()
    {
        ObjectHash root = Facts.Hash(1);
        ObjectHash a = Facts.Hash(2);
        ObjectHash b = Facts.Hash(3);
        ObjectHash c = Facts.Hash(4);
        ObjectHash stray = Facts.Hash(5);
        ObjectHash orphan = Facts.Hash(6);
        var replyTo = new Dictionary<ObjectHash, ObjectHash>
        {
            [root] = default,
            [a] = root,
            [b] = a,
            [c] = root,
            [stray] = default,
            [orphan] = Facts.Hash(0x77),
        };
        var cycle = new Dictionary<ObjectHash, ObjectHash> { [a] = b, [b] = a };

        Assert.Equal(root, Threading.RootOf(b, replyTo, ChatLimits.ThreadDepth));
        Assert.Equal(root, Threading.RootOf(root, replyTo, ChatLimits.ThreadDepth));
        Assert.Equal(orphan, Threading.RootOf(orphan, replyTo, ChatLimits.ThreadDepth));
        Assert.Equal(a, Threading.RootOf(b, replyTo, maxDepth: 1));
        Assert.Equal(new HashSet<ObjectHash> { a, b, c }, Threading.Replies(root, replyTo));
        Assert.Empty(Threading.Replies(stray, replyTo));
        Assert.Contains(Threading.RootOf(a, cycle, ChatLimits.ThreadDepth), new[] { a, b });
    }
}
