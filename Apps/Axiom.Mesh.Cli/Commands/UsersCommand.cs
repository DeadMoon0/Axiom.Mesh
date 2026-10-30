using System.Collections.Generic;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh users [--json]</c>: every known user, the own one included (LOCAL-SPEC.md §6.2).</summary>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record UsersCommand(bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        IReadOnlyList<User> users = await context.SendAsync(new ListKnownUsers()).ConfigureAwait(false);
        return Json ? Print(context, UsersView.Json(users)) : Print(context, UsersView.Text(users));
    }
}
