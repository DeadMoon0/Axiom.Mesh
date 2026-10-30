using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Host.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Host.Tests.Rules;

/// <summary>
/// The daemon on this machine's real pipe (LOCAL-SPEC.md §2, §4.2, §6.1): it starts with a fresh home and a pipe of its
/// own, a client of the same user connects through <c>LocalPipe</c> and is told there is no identity, creates one, finds
/// it present, and leaves; the keys survive a restart; a second daemon on the same pipe does not start.
/// </summary>
public sealed class RealPipeTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task AClientOfTheSameUserCreatesAnIdentityOverTheRealPipe_ItSurvivesARestart_ASecondDaemonOnThePipeDoesNotStart()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            return;

        using var home = new TempHome();
        using var other = new TempHome();
        string pipe = OperatingSystem.IsWindows() ? "axiom-mesh-test-" + Guid.NewGuid().ToString("N")[..12] : Path.Combine(home.Path, "axiom-mesh.sock");
        await WriteQuietConfigAsync(home);
        await WriteQuietConfigAsync(other);
        Func<string, string?> environment = name => name == PipeLocation.EnvironmentVariable ? pipe : Environment.GetEnvironmentVariable(name);
        PipeLocation location = PipeLocation.Resolve(HostPlatform.Current, new PipeEnvironment { Pipe = pipe });

        HostRuntime daemon = await HostRuntime.StartAsync(Start(home, environment), None);
        try
        {
            Identity created;
            await using (LocalClient client = await ConnectAsync(location))
            {
                Assert.False(client.Host.Identity.Present);
                Assert.Equal(0ul, client.Host.StoreSeq);
                Assert.Equal(HostCapabilities.StoreReadable, client.Host.Capabilities); // LOCAL-SPEC.md §14.4: the store directory is the same user's
                Assert.False((await client.SendAsync(new GetIdentity(), None)).Value.Present);

                created = (await client.SendAsync(new CreateIdentity { UserName = "Alice", DeviceName = "test-pc" }, None)).Value;
                Assert.True(created.Present);
                Assert.True(created.UserKeyPresent);
                Assert.Equal(1ul, created.RosterSeq);
                Assert.Equal(created, (await client.SendAsync(new GetIdentity(), None)).Value);
                Assert.Equal(created.DeviceId, daemon.Node.Device);
                Assert.Equal(Status.AlreadyExists, (await client.SendAsync(new CreateIdentity { UserName = "Alice", DeviceName = "test-pc" }, None)).Status);
            }

            var refused = await Assert.ThrowsAsync<HostException>(() => HostRuntime.StartAsync(Start(other, environment), None));
            Assert.Equal(HostExitCode.PipeTaken, refused.Exit);
            Assert.NotEmpty(refused.RecoverySteps);

            await daemon.DisposeAsync();
            daemon = await HostRuntime.StartAsync(Start(home, environment), None);
            await using (LocalClient again = await ConnectAsync(location))
            {
                Assert.Equal(created, again.Host.Identity);
            }
        }
        finally
        {
            await daemon.DisposeAsync();
        }
    }

    private static HostStart Start(TempHome home, Func<string, string?> environment) => new()
    {
        Paths = home.Paths,
        Environment = environment,
        Version = "0.1.0-test",
        ConsoleLog = false,
    };

    // A daemon of the tests binds no fixed port and announces nothing on the LAN.
    private static Task WriteQuietConfigAsync(TempHome home) =>
        File.WriteAllTextAsync(home.Paths.ConfigFile, "{ \"mesh\": { \"port\": 0, \"listen\": \"127.0.0.1\", \"mdns\": false, \"broadcast\": false } }");

    private static async Task<LocalClient> ConnectAsync(PipeLocation location)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Stream stream = await LocalPipe.ConnectAsync(location, deadline.Token);
        return await LocalClient.OpenAsync(stream, new LocalClientOptions { Name = "real-pipe-test" }, deadline.Token);
    }
}
