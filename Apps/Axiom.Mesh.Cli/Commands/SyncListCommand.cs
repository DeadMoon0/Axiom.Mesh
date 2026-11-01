using System.Threading.Tasks;
using Axiom.Mesh.Sync;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh sync list [--json]</c>: every folder the sync service keeps in sync (SYNC-SERVICE.md §5.1).</summary>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record SyncListCommand(bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        BindingsReply bindings = await context.SyncAsync(new BindingsRequest()).ConfigureAwait(false);
        return Json ? Print(context, SyncView.BindingsJson(bindings.Bindings)) : Print(context, SyncView.Bindings(bindings.Bindings));
    }
}
