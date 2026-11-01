using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Sync.Tests.Conventions;

/// <summary>
/// The rules of the sync service are honest (the user's rule, SYNC-SERVICE.md §11): every file under <c>Rules/</c> takes data and
/// returns data. None touches the disk, the clock, the Host or a task.
/// </summary>
public sealed class PureRulesTests
{
    private static readonly string[] Forbidden =
    [
        @"\bSystem\.IO\b", @"\bFile\.", @"\bDirectory\.", @"\bPath\.", @"\bTimeProvider\b", @"\bDateTime(Offset)?\.(Utc)?Now\b",
        @"\bLocalClient\b", @"\bServiceSession\b", @"\bTask\b", @"\bEnvironment\.", @"\bRandom\b", @"\bStopwatch\b",
    ];

    [Fact]
    public void NoRuleTouchesTheDiskTheClockOrTheHost()
    {
        string folder = Path.Combine(Repository.ProjectFolder("Axiom.Mesh.Sync"), "Rules");
        var offenders = Repository.SourceFiles(folder, "*.cs")
            .SelectMany(file => Forbidden.Where(pattern => Regex.IsMatch(Code(File.ReadAllText(file)), pattern)).Select(pattern => $"{Path.GetFileName(file)} uses {pattern}"))
            .ToList();

        Assert.True(Directory.GetFiles(folder, "*.cs").Length >= 20, "The rules folder moved; the convention reads " + folder);
        Assert.True(offenders.Count == 0, "Rules that are not pure: " + string.Join("; ", offenders));
    }

    // The code without its comments: a doc comment may name what the rule does not touch.
    private static string Code(string source) => string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", System.StringComparison.Ordinal)));
}
