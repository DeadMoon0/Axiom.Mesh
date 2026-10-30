using Axiom.Mesh.Core;

namespace Axiom.Mesh.Host.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §2.2: the pipe admits the operating-system user the daemon serves and nobody else; a daemon that runs
/// as a system service admits the users its allow list names besides; an identity whose user could not be read is
/// refused.
/// </summary>
public sealed class PipeAdmissionTests
{
    [Theory]
    [InlineData("AzureAD\\alice", "AzureAD\\alice", "", false, true)] // the served user
    [InlineData("azuread\\ALICE", "AzureAD\\alice", "", false, true)] // Windows account names compare without case
    [InlineData("1000", "1000", "", false, true)] // the served uid on Linux
    [InlineData("AzureAD\\mallory", "AzureAD\\alice", "", false, false)] // another user of the same machine
    [InlineData("1001", "1000", "", false, false)] // another uid
    [InlineData("AzureAD\\bob", "AzureAD\\alice", "AzureAD\\bob", false, false)] // an allow list means nothing to a daemon that runs as a user
    [InlineData("AzureAD\\bob", "NT AUTHORITY\\SYSTEM", "AzureAD\\bob", true, true)] // a system service admits its allow list
    [InlineData("1001", "0", "1001", true, true)] // so does a root daemon on Linux
    [InlineData("AzureAD\\mallory", "NT AUTHORITY\\SYSTEM", "AzureAD\\bob", true, false)] // but nobody it does not list
    [InlineData("(unknown)", "AzureAD\\alice", "", false, false)] // a user that could not be read is nobody the daemon serves
    public void Decide_AnotherOsUserIsRefused_TheServedUserAdmitted_AnAllowListedUserAdmittedForASystemService(string user, string hostUser, string allowed, bool service, bool admitted)
    {
        string[] allowUsers = allowed.Length == 0 ? [] : [allowed];

        PipeAdmissionVerdict verdict = PipeAdmission.Decide(ProcessIdentity.Pipe(4242, user, "/usr/bin/app"), hostUser, allowUsers, service);

        Assert.Equal(admitted, verdict.IsAdmitted);
        Assert.Equal(admitted, verdict.Refusal is null);
    }

    // An in-process service never comes through the pipe: its identity is refused there.
    [Fact]
    public void AnInProcessIdentityIsNeverAdmittedOnThePipe()
    {
        Assert.False(PipeAdmission.Decide(ProcessIdentity.InProcess("sync"), "alice", [], runningAsService: false).IsAdmitted);
    }
}
