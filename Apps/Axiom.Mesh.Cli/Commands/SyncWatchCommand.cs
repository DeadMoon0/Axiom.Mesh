using System;
using System.IO;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Sync;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh sync watch [&lt;dir&gt;]</c>: prints every run and conflict of a bound folder, or of every binding, as the sync
/// service pushes them (SYNC-SERVICE.md §6), until Ctrl-C. A lapse is reported and the watch goes on.
/// </summary>
/// <param name="Directory">A bound folder, as typed; <see langword="null"/> for every binding.</param>
internal sealed record SyncWatchCommand(string? Directory) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        string? only = Directory is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(context.Environment.CurrentDirectory, Directory)));
        LocalClient client = await context.UserAsync().ConfigureAwait(false);
        context.Error.WriteLine(WatchCommand.Started);
        try
        {
            while (true)
            {
                Subscription<ServiceEvent> events = await context.SubscribeAsync<ServiceEvent>(client, SyncEventReader.Filter).ConfigureAwait(false);
                try
                {
                    await foreach (ServiceEvent received in events.WithCancellation(context.Cancellation).ConfigureAwait(false))
                    {
                        if (!SyncEventReader.TryRead(received, out SyncEvent? sync) || SyncView.WatchLine(sync, only) is not { } line)
                            continue;
                        context.Output.WriteLine(line);
                        await context.Output.FlushAsync(context.Cancellation).ConfigureAwait(false);
                    }

                    context.Error.WriteLine($"the watch fell behind and missed {events.Lapse?.Dropped ?? 0} events; 'axiom-mesh sync status' shows where each binding stands; watching again");
                }
                finally
                {
                    await events.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (context.Cancellation.IsCancellationRequested)
        {
            return CliExitCode.Ok;
        }
    }
}
