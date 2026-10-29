using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §2.2 and §13: every request that spends the user key is logged with the process id and the executable
/// of its connection, whether it succeeded or not, and the configuration may confine them to listed executables; from
/// any other executable such a request is answered <c>NotPermitted</c>.
/// </summary>
public sealed class UserKeyRequestTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task EveryUserKeyRequestIsLoggedWithPidAndPath_AndOneFromAnUnlistedExecutableIsNotPermitted()
    {
        var log = new CapturedLog();
        const string Listed = "/usr/bin/axiom-mesh";
        await using TestMesh mesh = await TestMesh.StartAsync(
            TestMeshOptions.Of(LocalMeshes.Node) with { Local = LocalPortOptions.Default with { ConfineUserKeyRequestsTo = [Listed] }, Loggers = log },
            None);
        TestNode node = mesh[LocalMeshes.Node];

        LocalClient script = await node.ConnectAsync(new LocalClientOptions { Name = "script" }, TestProcess.Served, None);
        var grant = new SetGrant { PackageId = default, GranteeId = default, Rights = GrantRights.Read };
        Assert.Equal(Status.NotPermitted, (await script.SendAsync(grant, None)).Status);
        Assert.True((await script.SendAsync(new GetIdentity(), None)).IsOk);

        LocalClient cli = await node.ConnectAsync(new LocalClientOptions { Name = "cli" }, TestProcess.Served.Running(Listed, 777), None);
        Assert.NotEqual(Status.NotPermitted, (await cli.SendAsync(grant, None)).Status);
        Assert.NotEqual(Status.NotPermitted, (await cli.SendAsync(new RevokeGrant { PackageId = default, GranteeId = default }, None)).Status);

        string[] lines = [.. log.Lines.Where(l => l.Message.StartsWith("user-key request", System.StringComparison.Ordinal)).Select(l => l.Message)];
        Assert.Equal(3, lines.Length);
        Assert.Contains("SetGrant (312) from pid 4242 \"/usr/bin/axiom-test\"", lines[0]);
        Assert.EndsWith(": NotPermitted", lines[0]);
        Assert.Contains("SetGrant (312) from pid 777 \"/usr/bin/axiom-mesh\"", lines[1]);
        Assert.Contains("RevokeGrant (313) from pid 777 \"/usr/bin/axiom-mesh\"", lines[2]);
    }
}
