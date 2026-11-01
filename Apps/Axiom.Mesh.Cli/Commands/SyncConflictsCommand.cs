using System.Collections.Generic;
using System.Threading.Tasks;
using Axiom.Mesh.Sync;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh sync conflicts [&lt;dir&gt;] [--json]</c>: every conflict the sync service recorded, oldest first
/// (SYNC-SERVICE.md §5.6, §7); without a directory, the current directory's binding, else every binding. A conflict is resolved
/// by deleting the copy not wanted.
/// </summary>
/// <param name="Directory">A path inside a bound folder, as typed; <see langword="null"/> for the current directory's.</param>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record SyncConflictsCommand(string? Directory, bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        string path = SyncPaths.Of(context, Directory);
        var conflicts = new List<Conflict>();
        ulong after = 0;
        while (true)
        {
            ConflictsReply page = await context.SyncAsync(new ConflictsRequest { Directory = path, After = after }).ConfigureAwait(false);
            conflicts.AddRange(page.Conflicts);
            if (!page.More || page.Conflicts.Count == 0)
                break;
            after = page.Conflicts[^1].Id;
        }

        return Json ? Print(context, SyncView.ConflictsJson(conflicts)) : Print(context, SyncView.Conflicts(conflicts));
    }
}
