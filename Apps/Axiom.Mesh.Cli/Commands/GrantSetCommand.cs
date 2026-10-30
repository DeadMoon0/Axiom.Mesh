using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh grant set &lt;package&gt; &lt;user&gt; [--write]</c>: lets a known user read, or also write, an own Package (LOCAL-SPEC.md §6.4).</summary>
/// <param name="Package">The Package as typed.</param>
/// <param name="User">The user as typed: id, prefix or name.</param>
/// <param name="Write">Whether the user may write too.</param>
internal sealed record GrantSetCommand(string Package, string User, bool Write) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        PackageId package = await IdResolver.PackageAsync(context, Package).ConfigureAwait(false);
        UserId user = await IdResolver.UserAsync(context, User).ConfigureAwait(false);
        Grant grant = await context.SendAsync(new SetGrant { PackageId = package, GranteeId = user, Rights = Write ? GrantRights.Write : GrantRights.Read }).ConfigureAwait(false);
        return Print(context, $"user {Formats.Short(user.Value)} may {PackagesView.Rights(grant.Rights)} package {Formats.Short(package.Value)} (grant seq {grant.Seq})");
    }
}
