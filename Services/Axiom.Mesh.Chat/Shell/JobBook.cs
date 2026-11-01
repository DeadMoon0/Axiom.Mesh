using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Chat;

/// <summary>
/// The service's jobs (CHAT-SERVICE.md §8, S-D8): numbered from 1 while the service runs, at most
/// <see cref="ChatLimits.MaxJobs"/> running, the last <see cref="ChatLimits.EndedJobsKept"/> ended ones remembered; a
/// change is handed back for reporting only when <see cref="JobRule.ShouldReport"/> says so on the injected clock.
/// Thread-safe; the jobs themselves run elsewhere.
/// </summary>
internal sealed class JobBook
{
    private readonly TimeProvider time;
    private readonly object gate = new();
    private readonly Dictionary<uint, (JobView View, DateTimeOffset? Reported)> running = [];
    private IReadOnlyList<JobView> ended = [];
    private uint next;

    /// <summary>A book on a clock.</summary>
    /// <param name="time">The service's clock.</param>
    public JobBook(TimeProvider time) => this.time = time;

    /// <summary>Starts a job, when one more may run.</summary>
    /// <param name="start">The job as it starts; its number is filled in.</param>
    /// <param name="started">The job with its number, to report.</param>
    /// <returns><see langword="false"/> when <see cref="ChatLimits.MaxJobs"/> run already.</returns>
    public bool TryStart(JobView start, out JobView started)
    {
        ArgumentNullException.ThrowIfNull(start);
        lock (gate)
        {
            if (!JobRule.MayStart(running.Count))
            {
                started = start;
                return false;
            }

            started = start with { Job = ++next, State = JobState.Running };
            running[started.Job] = (started, time.GetUtcNow());
            return true;
        }
    }

    /// <summary>Records a running job's progress.</summary>
    /// <param name="job">The job.</param>
    /// <param name="done">Bytes done.</param>
    /// <param name="total">Bytes in all, 0 while unknown.</param>
    /// <returns>The job to report now, or <see langword="null"/> when the report is coalesced.</returns>
    public JobView? Progress(uint job, ulong done, ulong total)
    {
        lock (gate)
        {
            if (!running.TryGetValue(job, out (JobView View, DateTimeOffset? Reported) entry))
                return null;
            JobView view = entry.View with { Done = done, Total = Math.Max(total, entry.View.Total) };
            DateTimeOffset now = time.GetUtcNow();
            bool report = JobRule.ShouldReport(entry.Reported, now, JobState.Running);
            running[job] = (view, report ? now : entry.Reported);
            return report ? view : null;
        }
    }

    /// <summary>Ends a job.</summary>
    /// <param name="job">The job.</param>
    /// <param name="end">What the ended job says, from the running one.</param>
    /// <returns>The ended job, always reported.</returns>
    public JobView End(uint job, Func<JobView, JobView> end)
    {
        ArgumentNullException.ThrowIfNull(end);
        lock (gate)
        {
            JobView last = running.TryGetValue(job, out (JobView View, DateTimeOffset? Reported) entry) ? entry.View : new JobView { Job = job };
            JobView ended = end(last);
            running.Remove(job);
            this.ended = JobRule.Remember(this.ended, ended);
            return ended;
        }
    }

    /// <summary>Every running job and the ended ones remembered, by number.</summary>
    /// <returns>The jobs.</returns>
    public IReadOnlyList<JobView> List()
    {
        lock (gate)
            return [.. running.Values.Select(r => r.View).Concat(ended).OrderBy(j => j.Job).TakeLast(ChatLimits.JobsListed)];
    }

    /// <inheritdoc />
    public override string ToString()
    {
        lock (gate)
            return $"{running.Count} jobs running, {ended.Count} ended";
    }
}
