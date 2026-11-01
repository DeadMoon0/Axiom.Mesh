using System.Collections.Generic;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh services [--json]</c>: the services the Host lists, how each proves its name, and whether it is connected (LOCAL-SPEC.md §5.12, §6.7).</summary>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record ServicesCommand(bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        IReadOnlyList<Service> services = await context.SendAsync(new ListServices()).ConfigureAwait(false);
        return Json ? Print(context, ServicesView.Json(services)) : Print(context, ServicesView.Text(services));
    }
}
