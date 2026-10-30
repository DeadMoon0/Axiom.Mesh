using System.Collections.Generic;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh devices [&lt;user&gt;] [--json]</c>: the devices of a user's current Roster, the own user's by default (LOCAL-SPEC.md §6.3).</summary>
/// <param name="User">The user as typed; <see langword="null"/> for the own user.</param>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record DevicesCommand(string? User, bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        UserId user = User is null ? default : await IdResolver.UserAsync(context, User).ConfigureAwait(false);
        IReadOnlyList<Device> devices = await context.SendAsync(new ListDevices { UserId = user }).ConfigureAwait(false);
        return Json ? Print(context, UsersView.DevicesJson(devices)) : Print(context, UsersView.DevicesText(devices));
    }
}
