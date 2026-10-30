using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh help [command]</c>: every command with one line, or one command's usage and options.</summary>
/// <param name="Topic">The words of the command asked about; empty for the overview.</param>
internal sealed record HelpCommand(IReadOnlyList<string> Topic) : Command
{
    /// <inheritdoc />
    public override Task<CliExitCode> RunAsync(CommandContext context)
    {
        if (Topic.Count == 0)
            return Task.FromResult(Print(context, CommandSpecs.Overview()));
        CommandSpec? spec = CommandSpecs.All.FirstOrDefault(s => s.Words.SequenceEqual(Topic));
        if (spec is not null)
            return Task.FromResult(Print(context, spec.HelpLines()));

        // A group word alone (pair, package): the commands of the group.
        CommandSpec[] group = [.. CommandSpecs.All.Where(s => s.Words.Count > Topic.Count && s.Words.Take(Topic.Count).SequenceEqual(Topic))];
        if (group.Length == 0)
            throw CliFailure.Usage($"'{string.Join(' ', Topic)}' is not a command; run 'axiom-mesh help' for the list");
        return Task.FromResult(Print(context, group.Select(s => s.Usage)));
    }
}
