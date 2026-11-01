using System;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Sync.Client.Tests.Rules;

/// <summary>
/// The sync service's statuses (SYNC-SERVICE.md §7, LOCAL-SPEC.md §11.2): service-defined codes from 32 768 up, one each, the
/// zero member first; each explained in one line; a code of LOCAL-SPEC.md §11.1 is no sync status.
/// </summary>
public sealed class SyncStatusTests
{
    [Fact]
    public void EverySyncStatusIsAServiceDefinedCodeWithItsOwnExplanation()
    {
        SyncStatus[] statuses = [.. Enum.GetValues<SyncStatus>().Where(s => s != SyncStatus.None)];

        Assert.Equal(SyncStatus.None, Enum.GetValues<SyncStatus>()[0]);
        Assert.Equal([32_768, 32_769, 32_770, 32_771, 32_772, 32_773, 32_774], statuses.Select(s => (int)s));
        Assert.All(statuses, s => Assert.True(ReplyStatusRule.MayReply((Status)(ushort)s), s.ToString()));
        Assert.Equal(statuses.Length, statuses.Select(SyncStatusText.Explain).Distinct().Count());
        Assert.All(statuses, s => Assert.True(SyncStatusText.IsSyncStatus((Status)(ushort)s, out SyncStatus read) && read == s));
        Assert.False(SyncStatusText.IsSyncStatus(Status.NotFound, out SyncStatus none));
        Assert.Equal(SyncStatus.None, none);
        Assert.False(SyncStatusText.IsSyncStatus((Status)40_000, out _));
    }
}
