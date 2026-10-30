using System.Collections.Generic;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh package list [--followed] [--json]</c>: the Packages listed to this user or followed here (LOCAL-SPEC.md §6.4).</summary>
/// <param name="Followed">Whether to list only the followed ones.</param>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record PackageListCommand(bool Followed, bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        var packages = new List<Package>();
        PackageId? after = null;
        while (true)
        {
            PackagePage page = await context.SendAsync(new ListPackages { After = after, FollowedOnly = Followed }).ConfigureAwait(false);
            packages.AddRange(page.Packages);
            if (!page.More || page.Packages.Count == 0)
                break;
            after = page.Packages[^1].PackageId;
        }

        return Json ? Print(context, PackagesView.Json(packages)) : Print(context, PackagesView.Text(packages));
    }
}
