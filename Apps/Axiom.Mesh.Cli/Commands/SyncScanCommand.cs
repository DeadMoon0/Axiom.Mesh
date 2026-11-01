using System.Globalization;
using System.Threading.Tasks;
using Axiom.Mesh.Sync;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh sync scan [&lt;dir&gt;]</c>: asks the sync service to look at a bound folder now instead of waiting for its watcher
/// (SYNC-SERVICE.md §5.5); without a directory, the current directory's binding, else every binding.
/// </summary>
/// <param name="Directory">A path inside a bound folder, as typed; <see langword="null"/> for the current directory's.</param>
internal sealed record SyncScanCommand(string? Directory) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        ScanReply queued = await context.SyncAsync(new ScanRequest { Directory = SyncPaths.Of(context, Directory) }).ConfigureAwait(false);
        return Print(context, $"a run is queued for {queued.Queued.ToString(CultureInfo.InvariantCulture)} {(queued.Queued == 1 ? "binding" : "bindings")}; 'axiom-mesh sync status' shows how it went");
    }
}
