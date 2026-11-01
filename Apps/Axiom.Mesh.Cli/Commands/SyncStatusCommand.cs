using System.Threading.Tasks;
using Axiom.Mesh.Sync;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh sync status [&lt;dir&gt;] [--json]</c>: where a bound folder stands (SYNC-SERVICE.md §5.4); without a directory,
/// the one the current directory lies in (the nearest marker file above it), else every binding.
/// </summary>
/// <param name="Directory">A path inside a bound folder, as typed; <see langword="null"/> for the current directory's.</param>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record SyncStatusCommand(string? Directory, bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        string path = SyncPaths.Of(context, Directory);
        StatusReply status = await context.SyncAsync(new StatusRequest { Directory = path }).ConfigureAwait(false);
        if (Json)
            return Print(context, SyncView.BindingsJson(status.Bindings));
        return status.Bindings.Count == 1 ? Print(context, SyncView.Status(status.Bindings[0])) : Print(context, SyncView.Bindings(status.Bindings));
    }
}
