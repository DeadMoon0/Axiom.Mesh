using System.Globalization;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh identity create --user &lt;name&gt; --device &lt;name&gt;</c>: a new user with this device as its first (LOCAL-SPEC.md §6.1).</summary>
/// <param name="User">The user's display name.</param>
/// <param name="Device">This device's name in the Roster.</param>
internal sealed record IdentityCreateCommand(string User, string Device) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        Identity created = await context.SendAsync(new CreateIdentity { UserName = User, DeviceName = Device }).ConfigureAwait(false);
        return Print(context, TextTable.Pairs(
        [
            ("Created", $"user {created.UserName}, device {created.DeviceName}"),
            ("User", created.UserId.Value.ToHex()),
            ("Device", created.DeviceId.Value.ToHex()),
            ("Roster", "seq " + created.RosterSeq.ToString(CultureInfo.InvariantCulture)),
        ]));
    }
}
