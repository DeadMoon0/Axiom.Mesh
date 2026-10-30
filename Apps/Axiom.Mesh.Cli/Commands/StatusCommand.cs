using System.Collections.Generic;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh status</c>: whether the Host runs, who this device is, the store and the peers.</summary>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record StatusCommand(bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        LocalClient client = await context.UserAsync().ConfigureAwait(false);
        Identity identity = await context.SendAsync(new GetIdentity()).ConfigureAwait(false);
        Store store = await context.SendAsync(new GetStore()).ConfigureAwait(false);
        IReadOnlyList<Peer> peers = await context.SendAsync(new ListPeers()).ConfigureAwait(false);
        string host = context.Environment.HostAddress;
        return Json
            ? Print(context, StatusView.Json(host, client.Host, identity, store, peers))
            : Print(context, StatusView.Text(host, client.Host, identity, store, peers));
    }
}
