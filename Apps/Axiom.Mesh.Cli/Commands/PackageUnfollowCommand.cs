using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh package unfollow &lt;package&gt;</c>: stop replicating a Package; what it pinned may be evicted (LOCAL-SPEC.md §6.4).</summary>
/// <param name="Package">The Package as typed: id, prefix or name.</param>
internal sealed record PackageUnfollowCommand(string Package) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        PackageId id = await IdResolver.PackageAsync(context, Package).ConfigureAwait(false);
        Package unfollowed = await context.SendAsync(new UnfollowPackage { PackageId = id }).ConfigureAwait(false);
        return Print(context, $"no longer following {(unfollowed.Name.Length == 0 ? Formats.Short(id.Value) : unfollowed.Name)} ({Formats.Short(id.Value)})");
    }
}
