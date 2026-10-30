using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh peer add &lt;host:port&gt;</c>: a static peer, dialled now and after every restart (LOCAL-SPEC.md §6.6).</summary>
/// <param name="Address">The address.</param>
internal sealed record PeerAddCommand(string Address) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        StaticPeer added = await context.SendAsync(new AddStaticPeer { Address = Address }).ConfigureAwait(false);
        return Print(context, $"static peer {added.Address} added; it is dialled now and after every restart");
    }
}
