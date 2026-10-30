using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh store [--json]</c>: the store's size, budget and what is pinned (LOCAL-SPEC.md §5.10).</summary>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record StoreCommand(bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        Store store = await context.SendAsync(new GetStore()).ConfigureAwait(false);
        return Json ? Print(context, StoreView.Json(store)) : Print(context, StoreView.Text(store));
    }
}
