using System;
using System.Linq;
using System.Threading.Tasks;
using Axiom.Mesh.Cli.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>
/// The command line explains every failure in one line and an exit code (fundamentals plan §2.7, quality bar): a Host that
/// does not run, and every status of LOCAL-SPEC.md §11.1, never a stack trace.
/// </summary>
public sealed class FailureTests
{
    /// <summary>Every status the local protocol defines (LOCAL-SPEC.md §11.1, §14.9).</summary>
    public static TheoryData<Status> Statuses => [.. Enum.GetValues<Status>()];

    [Fact]
    public async Task AHostThatIsNotRunningIsExplainedNotStackTraced()
    {
        (CliExitCode exit, string output, string error) = await Offline.RunAsync("status");

        Assert.Equal(CliExitCode.HostNotReachable, exit);
        Assert.Equal(2, (int)exit);
        Assert.Empty(output);
        string line = Assert.Single(error.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(Offline.Pipe, line, StringComparison.Ordinal);
        Assert.Contains("axiom-meshd run", line, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", line, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public void EveryStatusCodeOfLocalSpec11HasAnExitCodeAndAOneLineExplanation(Status status)
    {
        CliExitCode exit = ExitCodes.Of(status);
        string line = StatusText.Explain(status, new Error { Message = "the Host's own words" }, "laptop");

        Assert.Equal(status == Status.Ok ? CliExitCode.Ok : CliExitCode.Refused, exit);
        Assert.False(string.IsNullOrWhiteSpace(StatusText.Meaning(status, "laptop")));
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.EndsWith(": the Host's own words", line, StringComparison.Ordinal);
        Assert.DoesNotContain("refused with status", line, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryStatusHasItsOwnExplanation_AndUserKeyAbsentNamesThisDeviceAndSaysToRunItThere()
    {
        string[] meanings = [.. Enum.GetValues<Status>().Select(s => StatusText.Meaning(s, "laptop"))];

        Assert.Equal(meanings.Length, meanings.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("the user key is on another device of this user, not on laptop; run this there", StatusText.Meaning(Status.UserKeyAbsent, "laptop"));
        Assert.Equal("the Host refused with status 300", StatusText.Meaning((Status)300, "laptop"));
    }

    // CHAT-SERVICE.md §7: every status the chat service defines exits 3 with its own line, the service's words after it.
    [Theory]
    [InlineData(32_768)] // NotAChannel
    [InlineData(32_769)] // NotAMessage
    [InlineData(32_770)] // NotYours
    [InlineData(32_771)] // AttachmentMissing
    [InlineData(32_772)] // Withdrawn
    [InlineData(32_773)] // JobNotFound
    public void EveryChatStatusExitsThreeWithItsOwnLine(int code)
    {
        var status = (Status)(ushort)code;
        CliFailure failure = CliFailure.Refused(status, new Error { Message = "the service's own words" }, "laptop");

        Assert.Equal(CliExitCode.Refused, failure.ExitCode);
        Assert.Equal($"{Chat.ChatStatusText.Explain((Chat.ChatStatus)code)}: the service's own words", failure.Line);
        Assert.DoesNotContain("refused with status", failure.Line, StringComparison.Ordinal);
    }
}
