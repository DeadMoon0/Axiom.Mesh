using System.Collections.Generic;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh sessions [--json]</c>: the established sessions and their traffic (LOCAL-SPEC.md §5.8).</summary>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record SessionsCommand(bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        IReadOnlyList<Session> sessions = await context.SendAsync(new ListSessions()).ConfigureAwait(false);
        return Json ? Print(context, PeersView.SessionsJson(sessions)) : Print(context, PeersView.SessionsText(sessions));
    }
}
