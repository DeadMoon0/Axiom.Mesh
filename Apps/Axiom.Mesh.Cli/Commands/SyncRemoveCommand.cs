using System.IO;
using System.Threading.Tasks;
using Axiom.Mesh.Sync;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh sync remove &lt;dir&gt;</c>: ends the binding of a folder (SYNC-SERVICE.md §5.3); the service stops keeping it in
/// sync, and every file stays where it is.
/// </summary>
/// <param name="Directory">The bound folder, absolute or relative to the current directory.</param>
internal sealed record SyncRemoveCommand(string Directory) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        string folder = Path.GetFullPath(Path.Combine(context.Environment.CurrentDirectory, Directory));
        UnbindReply unbound = await context.SyncAsync(new UnbindRequest { Directory = folder }).ConfigureAwait(false);
        return Print(context, $"{unbound.Binding.Directory} is no longer kept in sync; its files stay");
    }
}
