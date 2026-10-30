using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh package follow &lt;package&gt;</c>: replicate a Package here from now on (LOCAL-SPEC.md §6.4).</summary>
/// <param name="Package">The Package as typed: id, prefix or name.</param>
internal sealed record PackageFollowCommand(string Package) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        PackageId id = await IdResolver.PackageAsync(context, Package).ConfigureAwait(false);
        Package followed = await context.SendAsync(new FollowPackage { PackageId = id }).ConfigureAwait(false);
        return Print(context, $"following {(followed.Name.Length == 0 ? Formats.Short(id.Value) : followed.Name)} ({Formats.Short(id.Value)})");
    }
}
