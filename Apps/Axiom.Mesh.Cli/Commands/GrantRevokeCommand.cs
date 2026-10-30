using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh grant revoke &lt;package&gt; &lt;user&gt;</c>: withdraws a user's Grant; they keep what they hold (LOCAL-SPEC.md §6.4).</summary>
/// <param name="Package">The Package as typed.</param>
/// <param name="User">The user as typed.</param>
internal sealed record GrantRevokeCommand(string Package, string User) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        PackageId package = await IdResolver.PackageAsync(context, Package).ConfigureAwait(false);
        UserId user = await IdResolver.UserAsync(context, User).ConfigureAwait(false);
        Grant revoked = await context.SendAsync(new RevokeGrant { PackageId = package, GranteeId = user }).ConfigureAwait(false);
        return Print(context, $"user {Formats.Short(user.Value)} no longer has package {Formats.Short(package.Value)} (grant seq {revoked.Seq})");
    }
}
