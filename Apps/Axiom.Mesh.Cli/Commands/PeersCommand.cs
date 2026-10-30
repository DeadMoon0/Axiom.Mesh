using System.Collections.Generic;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh peers [--discovered] [--json]</c>: the devices this Host knows of and where each stands, or with
/// <c>--discovered</c> what discovery heard (LOCAL-SPEC.md §6.6).
/// </summary>
/// <param name="Discovered">Whether to list what discovery heard.</param>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record PeersCommand(bool Discovered, bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        IReadOnlyList<Peer> peers = Discovered
            ? await context.SendAsync(new ListDiscovered()).ConfigureAwait(false)
            : await context.SendAsync(new ListPeers()).ConfigureAwait(false);
        return Json ? Print(context, PeersView.Json(peers)) : Print(context, PeersView.Text(peers));
    }
}
