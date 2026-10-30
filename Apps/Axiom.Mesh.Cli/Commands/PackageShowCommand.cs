using System.Collections.Generic;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh package show &lt;package&gt; [--json]</c>: one Package and its Grants (LOCAL-SPEC.md §6.4).</summary>
/// <param name="Package">The Package as typed: id, prefix or name.</param>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record PackageShowCommand(string Package, bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        PackageId id = await IdResolver.PackageAsync(context, Package).ConfigureAwait(false);
        Package package = await context.SendAsync(new GetPackage { PackageId = id }).ConfigureAwait(false);
        IReadOnlyList<Grant> grants = await context.SendAsync(new ListGrants { PackageId = id }).ConfigureAwait(false);
        return Json ? Print(context, PackagesView.ShowJson(package, grants)) : Print(context, PackagesView.ShowText(package, grants));
    }
}
