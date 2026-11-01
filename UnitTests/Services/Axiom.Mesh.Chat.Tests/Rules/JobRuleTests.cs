using System;
using System.Linq;

namespace Axiom.Mesh.Chat.Tests.Rules;

/// <summary>
/// Jobs (CHAT-SERVICE.md §8, S-D8): sixteen run at once and the seventeenth is Busy; the last 48 ended ones are remembered;
/// progress is reported at most once a second, a start and an end always.
/// </summary>
public sealed class JobRuleTests
{
    // §8: 16 may run, not 17; the 49th ended job pushes out the oldest; progress waits a second, an end never waits.
    [Fact]
    public void SixteenJobsRun_TheSeventeenthIsBusy_FortyEightEndedAreKept_ProgressAtMostOnceASecond()
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch.AddDays(20_000);

        Assert.True(JobRule.MayStart(ChatLimits.MaxJobs - 1));
        Assert.False(JobRule.MayStart(ChatLimits.MaxJobs));
        var ended = Enumerable.Range(1, ChatLimits.EndedJobsKept).Select(i => new JobView { Job = (uint)i }).ToList();
        Assert.Equal(Enumerable.Range(2, ChatLimits.EndedJobsKept).Select(i => (uint)i), JobRule.Remember(ended, new JobView { Job = 49 }).Select(j => j.Job));
        Assert.True(JobRule.ShouldReport(null, start, JobState.Running));
        Assert.False(JobRule.ShouldReport(start, start.AddMilliseconds(999), JobState.Running));
        Assert.True(JobRule.ShouldReport(start, start.AddSeconds(1), JobState.Running));
        Assert.True(JobRule.ShouldReport(start, start.AddMilliseconds(1), JobState.Done));
        Assert.True(JobRule.ShouldReport(start, start, JobState.Failed));
    }
}
