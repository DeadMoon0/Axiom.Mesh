using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Discovery.Tests.Support;

/// <summary>A mechanism that reports a fixed list once and ends; counts the announcements it was asked for.</summary>
internal sealed class ListedDiscovery(params Announcement[] announcements) : IDiscovery
{
    public int Announced { get; private set; }

    public async IAsyncEnumerable<Announcement> ObserveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (Announcement announcement in announcements)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return announcement;
        }
    }

    public ValueTask AnnounceAsync(CancellationToken cancellationToken)
    {
        Announced++;
        return ValueTask.CompletedTask;
    }
}
