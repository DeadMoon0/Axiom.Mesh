using System;
using System.Linq;
using System.Threading.Tasks;
using Axiom.Mesh.Chat;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// Waiting for a job of the chat service (CHAT-SERVICE.md §8): the command subscribes to the service's events before it
/// starts the job, so the job's end is never missed, and reads <c>job</c> events until its job ended; a lapsed
/// subscription is looked up with <c>jobs</c> and subscribed again.
/// </summary>
internal static class ChatJobWait
{
    /// <summary>Subscribes to the chat service's events: every Channel's and the jobs'.</summary>
    /// <param name="context">The command's context.</param>
    /// <returns>The live subscription.</returns>
    public static async Task<Subscription<ServiceEvent>> SubscribeAsync(CommandContext context) =>
        await context.SubscribeAsync<ServiceEvent>(await context.UserAsync().ConfigureAwait(false), ChatEventReader.FilterOf(default)).ConfigureAwait(false);

    /// <summary>Subscribes, starts a job and waits until it ended: the done job, or a <see cref="CliFailure"/>.</summary>
    /// <param name="context">The command's context.</param>
    /// <param name="start">Starts the job with a chat call and returns its number.</param>
    /// <returns>The done job.</returns>
    public static async Task<JobView> RunAsync(CommandContext context, Func<Task<uint>> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        Subscription<ServiceEvent> events = await SubscribeAsync(context).ConfigureAwait(false);
        uint job;
        try
        {
            job = await start().ConfigureAwait(false);
        }
        catch
        {
            await events.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return await WaitAsync(context, events, job).ConfigureAwait(false);
    }

    /// <summary>Waits until a job ended, and returns it done; a failed job leaves as a <see cref="CliFailure"/>.</summary>
    /// <param name="context">The command's context.</param>
    /// <param name="events">The subscription made before the job started; disposed when the wait ends.</param>
    /// <param name="job">The job's number.</param>
    /// <returns>The done job.</returns>
    public static async Task<JobView> WaitAsync(CommandContext context, Subscription<ServiceEvent> events, uint job)
    {
        Subscription<ServiceEvent> current = events;
        try
        {
            while (true)
            {
                await foreach (ServiceEvent received in current.WithCancellation(context.Cancellation).ConfigureAwait(false))
                {
                    if (ChatEventReader.TryRead(received, out ChatEvent? chat) && chat is JobEvent { Job: { State: not JobState.Running } ended } && ended.Job == job)
                        return Done(ended);
                }

                JobsReply jobs = await context.ChatAsync(new JobsRequest()).ConfigureAwait(false);
                if (jobs.Jobs.FirstOrDefault(j => j.Job == job) is { State: not JobState.Running } known)
                    return Done(known);
                await current.DisposeAsync().ConfigureAwait(false);
                current = await SubscribeAsync(context).ConfigureAwait(false);
            }
        }
        finally
        {
            await current.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static JobView Done(JobView job)
    {
        if (job.State == JobState.Done)
            return job;
        string what = job.Kind == JobKind.Get ? "fetching the attachment" : $"attaching {job.Path}";
        string why = StatusText.Meaning((Status)job.Status, string.Empty);
        throw CliFailure.Problem($"{what} failed: {why}{(job.Problem.Length == 0 ? string.Empty : ": " + job.Problem)}");
    }
}
