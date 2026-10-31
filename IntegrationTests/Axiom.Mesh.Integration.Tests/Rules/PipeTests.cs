using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Host;
using Axiom.Mesh.Integration.Tests.Support;
using Axiom.Mesh.Local;
using Axiom.Mesh.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axiom.Mesh.Integration.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §2.1, §2.2 on the operating system itself: the pipe admits the Host's own user and nobody else, by the
/// access list the platform enforces and by the process identity the Host reads, and the Host learns who connected from
/// the platform (the client's pid and user), never from the client.
/// </summary>
public sealed class PipeTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §2.2: the Host reads the client's pid and user from the platform (GetNamedPipeClientProcessId and the impersonated
    // user on Windows, SO_PEERCRED on Linux): the admitted connection names this test process and its user.
    [Fact]
    public async Task TheHostLearnsTheClientsPidAndUserFromThePlatform()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            return;
        await using RealMeshFixture mesh = await RealMeshFixture.StartAsync(["alice-laptop"], [], [], None);
        long before = mesh.Node("alice-laptop").Facts.LastSequence;

        LocalClient client = await mesh.ConnectUserAsync("alice-laptop", "pid probe", None);
        Assert.True((await client.SendAsync(new GetIdentity(), None)).IsOk);

        NodeFact admitted = mesh.Node("alice-laptop").Facts.Recent().Single(f => f.Sequence > before && f.Kind == NodeFactKind.LocalAdmitted);
        Assert.Contains($"pid {Environment.ProcessId} ", admitted.Detail, StringComparison.Ordinal);
        Assert.Contains($"as \"{CurrentUser()}\"", admitted.Detail, StringComparison.Ordinal);
    }

    // §2.1 on Windows: the pipe rejects remote clients, so a connection through the machine's own name (the network
    // redirector, as a remote machine would come) is refused and never reaches the Host.
    [OsFact("windows")]
    public async Task OnWindowsAConnectionThroughTheMachinesNameIsRefused()
    {
        if (!OperatingSystem.IsWindows())
            return;
        await using RealMeshFixture mesh = await RealMeshFixture.StartAsync(["alice-laptop"], [], [], None);
        long before = mesh.Node("alice-laptop").Facts.LastSequence;
        using var remote = new NamedPipeClientStream(Environment.MachineName, mesh["alice-laptop"].Pipe.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        Exception? refused = await Record.ExceptionAsync(() => remote.ConnectAsync(5_000, None));

        Assert.True(refused is UnauthorizedAccessException or IOException or TimeoutException, $"A connection through \\\\{Environment.MachineName} was accepted ({refused?.GetType().Name ?? "no exception"}).");
        Assert.DoesNotContain(mesh.Node("alice-laptop").Facts.Recent(), f => f.Sequence > before && f.Kind is NodeFactKind.LocalAdmitted);
    }

    // §2.2 on Windows: the pipe's access list is protected, owned by the Host's account, and allows that account alone;
    // another account is refused by the system before the Host sees a byte.
    [OsFact("windows")]
    public async Task OnWindowsThePipesAccessListAllowsTheHostsAccountAlone()
    {
        if (!OperatingSystem.IsWindows())
            return;
        await using RealMeshFixture mesh = await RealMeshFixture.StartAsync(["alice-laptop"], [], [], None);
        using var client = new NamedPipeClientStream(".", mesh["alice-laptop"].Pipe.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5_000, None);

        AssertTheCurrentAccountAlone(client.GetAccessControl());
    }

    // §2.2 on Windows end to end needs a second account's token, which only its password or an elevated process gives.
    [Fact(Skip = "Windows: connecting as another account needs that account's password (LogonUser) or elevation; neither belongs in a test. The access list is checked by OnWindowsThePipesAccessListAllowsTheHostsAccountAlone and the identity decision by Host.Tests PipeAdmissionTests.")]
    public void OnWindowsAnotherAccountIsRefused()
    {
    }

    // §2.1 on Linux: the socket file is the user's alone (0600), and a socket file nobody listens on any more (a Host that
    // crashed) is taken over, while one a running Host serves is not.
    [OsFact("linux")]
    public async Task OnLinuxTheSocketIs0600_AStaleOneIsTakenOver_ALiveOneIsNot()
    {
        if (!OperatingSystem.IsLinux())
            return;
        string directory = Directory.CreateTempSubdirectory("axiom-mesh-uds-").FullName;
        try
        {
            string path = Path.Combine(directory, "stale.sock");
            await LeaveStaleSocketAsync(path);
            Assert.True(File.Exists(path));
            PipeLocation location = PipeLocation.Resolve(LocalPlatform.Linux, new PipeEnvironment { Pipe = path });

            await using PipeHost taken = PipeHost.Open(location, [], TimeProvider.System, NullLogger.Instance);
            Exception? second = Record.Exception(() => PipeHost.Open(location, [], TimeProvider.System, NullLogger.Instance));

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            Assert.IsAssignableFrom<MeshException>(second);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // §2.2 on Linux end to end (STRUCTURAL-DEBT.md 37): user nobody cannot open the 0600 socket (the access list), and
    // when the file is opened up to everyone, the Host still closes nobody's connection without a byte (process identity
    // by SO_PEERCRED), while the Host's own user is served throughout.
    [LinuxWithSudoFact]
    public async Task OnLinuxAnotherUserIsRefusedByTheSocketsModeAndByProcessIdentity()
    {
        if (!OperatingSystem.IsLinux())
            return;
        await using RealMeshFixture mesh = await RealMeshFixture.StartAsync(["alice-laptop"], [], [], None);
        string path = mesh["alice-laptop"].Pipe.Path;
        OpenUp(Path.GetDirectoryName(path)!);

        (int code, string said) = await AsNobodyAsync(path);
        Assert.True(code != 0 && said.Contains("PermissionError", StringComparison.Ordinal), $"nobody reached the 0600 socket: exit {code}, {said}");

        File.SetUnixFileMode(path, (UnixFileMode)0b110_110_110);
        (code, said) = await AsNobodyAsync(path);
        Assert.True(code == 0 && said.Contains("closed without a byte", StringComparison.Ordinal), $"nobody was not closed without a byte: exit {code}, {said}");
        Assert.True((await (await mesh.ConnectUserAsync("alice-laptop", "the host's own user", None)).SendAsync(new GetIdentity(), None)).IsOk);
    }

    [SupportedOSPlatform("windows")]
    private static void AssertTheCurrentAccountAlone(PipeSecurity security)
    {
        SecurityIdentifier self = WindowsIdentity.GetCurrent().User!;
        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(self, security.GetOwner(typeof(SecurityIdentifier)));
        foreach (PipeAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).OfType<PipeAccessRule>())
            Assert.Equal((self, AccessControlType.Allow), ((SecurityIdentifier)rule.IdentityReference, rule.AccessControlType));
        Assert.NotEmpty(security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)));
    }

    private static string CurrentUser() => OperatingSystem.IsWindows()
        ? WindowsIdentity.GetCurrent().Name
        : LinuxUserId().ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static uint LinuxUserId()
    {
        using Process id = Process.Start(new ProcessStartInfo("id", "-u") { RedirectStandardOutput = true })!;
        string text = id.StandardOutput.ReadToEnd();
        id.WaitForExit();
        return uint.Parse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    // Every directory from the socket's up to the temporary folder may be entered by others, so that only the socket's
    // own mode decides.
    [SupportedOSPlatform("linux")]
    private static void OpenUp(string directory)
    {
        string top = Path.GetTempPath().TrimEnd('/');
        for (string? at = directory; at is not null && at.Length > top.Length; at = Path.GetDirectoryName(at))
            File.SetUnixFileMode(at, File.GetUnixFileMode(at) | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    // A process binds the path and exits without unlinking it, as a crashed Host would: the file stays, nobody listens.
    // (A .NET socket unlinks its own file when disposed, so another runtime leaves it behind.)
    private static async Task LeaveStaleSocketAsync(string path)
    {
        var start = new ProcessStartInfo("python3") { RedirectStandardError = true };
        foreach (string argument in new[] { "-c", "import socket,sys; socket.socket(socket.AF_UNIX).bind(sys.argv[1])", path })
            start.ArgumentList.Add(argument);
        using Process crashed = Process.Start(start)!;
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        await crashed.WaitForExitAsync(deadline.Token);
        Assert.True(crashed.ExitCode == 0, await crashed.StandardError.ReadToEndAsync());
    }

    // Connects to the socket as user nobody (sudo -n -u nobody, which GitHub's Ubuntu runners allow without a password)
    // and reports how it went: a PermissionError, or "closed without a byte" when the Host ended the connection unanswered.
    private static async Task<(int Code, string Said)> AsNobodyAsync(string path)
    {
        const string Probe = "import socket,sys\ns=socket.socket(socket.AF_UNIX)\ns.connect(sys.argv[1])\ns.settimeout(10)\nprint('closed without a byte' if s.recv(1)==b'' else 'answered')";
        var start = new ProcessStartInfo("sudo") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-n", "-u", "nobody", "python3", "-c", Probe, path })
            start.ArgumentList.Add(argument);
        using Process probe = Process.Start(start)!;
        Task<string> output = probe.StandardOutput.ReadToEndAsync();
        Task<string> error = probe.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        await probe.WaitForExitAsync(deadline.Token);
        return (probe.ExitCode, await output + await error);
    }
}
