using System.Threading.Tasks;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh version</c> (or <c>--version</c>): the tool's version; the Host is not asked.</summary>
internal sealed record VersionCommand : Command
{
    /// <inheritdoc />
    public override Task<CliExitCode> RunAsync(CommandContext context) => Task.FromResult(Print(context, context.Environment.Version));
}
