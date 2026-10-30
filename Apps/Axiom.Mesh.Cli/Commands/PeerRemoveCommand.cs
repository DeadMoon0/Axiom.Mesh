using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh peer remove &lt;host:port&gt;</c>: no longer a static peer (LOCAL-SPEC.md §6.6).</summary>
/// <param name="Address">The address.</param>
internal sealed record PeerRemoveCommand(string Address) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        await context.SendAsync(new RemoveStaticPeer { Address = Address }).ConfigureAwait(false);
        return Print(context, $"static peer {Address} removed");
    }
}
