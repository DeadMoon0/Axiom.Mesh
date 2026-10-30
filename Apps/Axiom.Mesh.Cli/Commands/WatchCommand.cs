using System;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh watch [&lt;package&gt;]</c>: prints every Entry this Host stores from now on, of one Package or of all,
/// as it arrives (LOCAL-SPEC.md §7.3 <c>EntryStored</c>), until Ctrl-C. A lapse is reported and the watch goes on.
/// </summary>
/// <param name="Package">The Package as typed; <see langword="null"/> for every Package.</param>
internal sealed record WatchCommand(string? Package) : Command
{
    /// <summary>The line written when the watch starts, to standard error.</summary>
    public const string Started = "watching; Ctrl-C ends";

    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        Filter filter = Package is null ? Filter.Any : new Filter { PackageId = await IdResolver.PackageAsync(context, Package).ConfigureAwait(false) };
        LocalClient client = await context.UserAsync().ConfigureAwait(false);

        // From the store sequence the Host had when this connection opened: nothing stored since is missed (§7.2).
        Filter from = filter with { FromSeq = client.Host.StoreSeq + 1 };
        context.Error.WriteLine(Started);
        try
        {
            while (true)
            {
                Subscription<EntryStored> entries = await context.SubscribeAsync<EntryStored>(client, from).ConfigureAwait(false);
                from = filter;
                try
                {
                    await foreach (EntryStored stored in entries.WithCancellation(context.Cancellation).ConfigureAwait(false))
                    {
                        context.Output.WriteLine(HistoryView.WatchLine(stored.Entry));
                        await context.Output.FlushAsync(context.Cancellation).ConfigureAwait(false);
                    }

                    context.Error.WriteLine($"the watch fell behind and missed {entries.Lapse?.Dropped ?? 0} entries; 'axiom-mesh history' lists them; watching again");
                }
                finally
                {
                    await entries.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (context.Cancellation.IsCancellationRequested)
        {
            return CliExitCode.Ok;
        }
    }
}
