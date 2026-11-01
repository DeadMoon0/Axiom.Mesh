using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Chat;

/// <summary>
/// The bounds and the reporting of jobs (CHAT-SERVICE.md §8, S-D8): at most <see cref="ChatLimits.MaxJobs"/> run at once
/// (one more is <c>Busy</c>); the last <see cref="ChatLimits.EndedJobsKept"/> ended ones are remembered; progress is
/// reported at most once a second on the service's clock, a start and an end always. Pure: the clock's reading is given.
/// </summary>
internal static class JobRule
{
    /// <summary>The least time between two progress reports of one job.</summary>
    public static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(1);

    /// <summary>Whether one more job may start.</summary>
    /// <param name="running">The jobs running now.</param>
    /// <returns><see langword="false"/> at <see cref="ChatLimits.MaxJobs"/>.</returns>
    public static bool MayStart(int running) => running < ChatLimits.MaxJobs;

    /// <summary>The ended jobs to remember once one more ended: the latest ones, oldest first.</summary>
    /// <param name="ended">The ended jobs remembered so far, oldest first.</param>
    /// <param name="justEnded">The job that just ended.</param>
    /// <returns>At most <see cref="ChatLimits.EndedJobsKept"/>, the oldest dropped first.</returns>
    public static IReadOnlyList<JobView> Remember(IReadOnlyList<JobView> ended, JobView justEnded)
    {
        ArgumentNullException.ThrowIfNull(ended);
        ArgumentNullException.ThrowIfNull(justEnded);
        return [.. ended.Append(justEnded).TakeLast(ChatLimits.EndedJobsKept)];
    }

    /// <summary>Whether a job's new state is reported now.</summary>
    /// <param name="lastReported">When it was last reported; <see langword="null"/> before its first report.</param>
    /// <param name="now">Now, on the service's clock.</param>
    /// <param name="state">The job's state.</param>
    /// <returns>Always for the first report and an end; for progress, once <see cref="ProgressInterval"/> passed.</returns>
    public static bool ShouldReport(DateTimeOffset? lastReported, DateTimeOffset now, JobState state) =>
        state != JobState.Running || lastReported is not { } last || now - last >= ProgressInterval;
}
