using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh unpin &lt;hash&gt;</c>: removes the user's pin on an object; the other reasons stay (LOCAL-SPEC.md §6.5).</summary>
/// <param name="Hash">The object's hash, or a prefix of a stored Entry's.</param>
internal sealed record UnpinCommand(string Hash) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        ObjectHash hash = await IdResolver.ObjectAsync(context, Hash).ConfigureAwait(false);
        ushort removed = await context.SendAsync(new Unpin { Hashes = [hash] }).ConfigureAwait(false);
        return Print(context, removed == 0 ? $"{hash.Value.ToHex()} had no pin of the user" : $"unpinned {hash.Value.ToHex()}");
    }
}
