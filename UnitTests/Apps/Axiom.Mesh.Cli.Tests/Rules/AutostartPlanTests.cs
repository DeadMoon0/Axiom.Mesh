using System;
using System.Linq;

namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>
/// The release design's autostart (fundamentals plan §6e): the Host is registered to run at the user's logon, on Windows
/// as a per-user scheduled task running <c>axiom-meshd run</c>, on Linux as a systemd user unit enabled and started; an
/// uninstall stops it and removes exactly that registration. The plan is decided before anything is touched.
/// </summary>
public sealed class AutostartPlanTests
{
    private static readonly AutostartTarget Windows = new()
    {
        Daemon = @"C:\Users\alice\AppData\Local\Programs\Axiom.Mesh\axiom-meshd.exe",
        Account = @"LAPTOP\alice",
        TemporaryDirectory = @"C:\Temp",
    };

    private static readonly AutostartTarget Linux = new()
    {
        Daemon = "/home/alice/.local/bin/axiom-meshd",
        UnitDirectory = "/home/alice/.config/systemd/user",
    };

    // §6e Windows: a task with a logon trigger for the user's own account, the user's interactive token at least privilege
    // (DPAPI and the owner-only pipe are the user's), no time limit, not stopped on batteries, one instance; the daemon runs
    // hidden behind conhost --headless with "run".
    [Fact]
    public void OnWindowsTheHostIsAPerUserLogonTaskRunningTheDaemonHiddenWithoutATimeLimit()
    {
        string xml = ScheduledTaskPlan.Definition(Windows);

        Assert.Contains(@"<LogonTrigger><Enabled>true</Enabled><UserId>LAPTOP\alice</UserId></LogonTrigger>", xml, StringComparison.Ordinal);
        Assert.Contains(@"<UserId>LAPTOP\alice</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel>", xml, StringComparison.Ordinal);
        Assert.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", xml, StringComparison.Ordinal);
        Assert.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>", xml, StringComparison.Ordinal);
        Assert.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>", xml, StringComparison.Ordinal);
        Assert.Contains("<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>", xml, StringComparison.Ordinal);
        Assert.Contains(@"<Command>%SystemRoot%\System32\conhost.exe</Command>", xml, StringComparison.Ordinal);
        Assert.Contains(@"<Arguments>--headless &quot;C:\Users\alice\AppData\Local\Programs\Axiom.Mesh\axiom-meshd.exe&quot; run</Arguments>", xml, StringComparison.Ordinal);
        Assert.Contains(@"<WorkingDirectory>C:\Users\alice\AppData\Local\Programs\Axiom.Mesh</WorkingDirectory>", xml, StringComparison.Ordinal);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-16\"?>", xml, StringComparison.Ordinal);
    }

    // §6e Windows install: the definition is written, registered under \Axiom.Mesh\axiom-meshd replacing an older one, and
    // deleted; then an older Host is ended and the task started. --no-start registers only.
    [Fact]
    public void AWindowsInstallRegistersReplacingAnOlderTask_ThenRestartsTheHost_NoStartOnlyRegisters()
    {
        AutostartPlan plan = ScheduledTaskPlan.Install(Windows, start: true);

        Assert.Equal(
        [
            @"write C:\Temp\axiom-meshd.task.xml",
            @"schtasks /Create /TN \Axiom.Mesh\axiom-meshd /XML C:\Temp\axiom-meshd.task.xml /F",
            @"delete C:\Temp\axiom-meshd.task.xml",
            @"schtasks /End /TN \Axiom.Mesh\axiom-meshd",
            @"stop every running C:\Users\alice\AppData\Local\Programs\Axiom.Mesh\axiom-meshd.exe",
            @"schtasks /Run /TN \Axiom.Mesh\axiom-meshd",
        ], plan.Steps.Select(s => s.Describe()));
        Assert.True(Assert.IsType<WriteStep>(plan.Steps[0]).Utf16);
        Assert.Equal([false, true, false], plan.Steps.OfType<RunStep>().Select(r => r.MayFail));
        Assert.Equal(AutostartSystem.ScheduledTask, plan.System);
        Assert.Equal(@"\Axiom.Mesh\axiom-meshd", plan.Registration);
        Assert.Equal(["run"], plan.DaemonArguments);

        AutostartPlan registerOnly = ScheduledTaskPlan.Install(Windows, start: false);
        Assert.False(registerOnly.Starts);
        Assert.Equal(["write", "schtasks", "delete"], registerOnly.Steps.Select(s => s.Describe().Split(' ')[0]));
    }

    // §6e Linux: a systemd user unit in the XDG unit directory running "axiom-meshd run", restarted on failure but not after
    // the daemon's own refusals (1 usage, 2 configuration, 3 pipe taken), enabled for default.target, so it starts at logon.
    [Fact]
    public void OnLinuxTheHostIsASystemdUserUnitEnabledForTheDefaultTarget()
    {
        string unit = SystemdUnitPlan.Unit(Linux);

        Assert.Contains("\nExecStart=\"/home/alice/.local/bin/axiom-meshd\" \"run\"\n", unit, StringComparison.Ordinal);
        Assert.Contains("\nType=simple\n", unit, StringComparison.Ordinal);
        Assert.Contains("\nRestart=on-failure\n", unit, StringComparison.Ordinal);
        Assert.Contains("\nRestartPreventExitStatus=1 2 3\n", unit, StringComparison.Ordinal);
        Assert.Contains("\n[Install]\nWantedBy=default.target\n", unit, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", unit, StringComparison.Ordinal);
        Assert.Equal("/home/alice/.config/systemd/user/axiom-meshd.service", SystemdUnitPlan.UnitFile(Linux).Replace('\\', '/'));
    }

    // §6e Linux install: write the unit, reload, enable, and restart it so a new binary runs; --no-start leaves out the restart.
    [Fact]
    public void ALinuxInstallWritesReloadsEnablesAndRestarts_NoStartLeavesOutTheRestart()
    {
        AutostartPlan plan = SystemdUnitPlan.Install(Linux, start: true);

        Assert.Equal(
        [
            "systemctl --user daemon-reload",
            "systemctl --user enable axiom-meshd.service",
            "systemctl --user restart axiom-meshd.service",
        ], plan.Steps.OfType<RunStep>().Select(r => r.Describe()));
        Assert.IsType<WriteStep>(plan.Steps[0]);
        Assert.False(Assert.IsType<WriteStep>(plan.Steps[0]).Utf16);
        Assert.All(plan.Steps.OfType<RunStep>(), r => Assert.False(r.MayFail));
        Assert.DoesNotContain(SystemdUnitPlan.Install(Linux, start: false).Steps.OfType<RunStep>(), r => r.Arguments.Contains("restart"));
    }

    // An uninstall stops the Host and removes the registration; what does not exist (a task never registered, a unit not
    // loaded) is no failure, and only the removal itself says whether something was registered.
    [Fact]
    public void AnUninstallStopsTheHostAndRemovesTheRegistration_WhatIsNotThereIsNoFailure()
    {
        AutostartPlan windows = ScheduledTaskPlan.Uninstall(Windows);
        Assert.Equal(
        [
            @"schtasks /End /TN \Axiom.Mesh\axiom-meshd",
            @"stop every running C:\Users\alice\AppData\Local\Programs\Axiom.Mesh\axiom-meshd.exe",
            @"schtasks /Delete /TN \Axiom.Mesh\axiom-meshd /F",
        ], windows.Steps.Select(s => s.Describe()));
        Assert.All(windows.Steps.OfType<RunStep>(), r => Assert.True(r.MayFail));
        Assert.Equal([false, true], windows.Steps.OfType<RunStep>().Select(r => r.Removes));

        AutostartPlan linux = SystemdUnitPlan.Uninstall(Linux);
        Assert.Equal(
        [
            "systemctl --user disable --now axiom-meshd.service",
            "delete " + SystemdUnitPlan.UnitFile(Linux),
            "systemctl --user daemon-reload",
            "systemctl --user reset-failed axiom-meshd.service",
        ], linux.Steps.Select(s => s.Describe()));
        Assert.True(Assert.IsType<DeleteStep>(linux.Steps[1]).Removes);
        Assert.Equal([true, false, true], linux.Steps.OfType<RunStep>().Select(r => r.MayFail));
    }

    // A home of its own (AXIOM_MESH_HOME when installing) is passed as --home, quoted for each system: a path with spaces,
    // and on systemd '%' and '$', which systemd would expand, are taken literally.
    [Fact]
    public void AHomeOfItsOwnIsPassedAsHome_QuotedSoThatEveryPathIsTakenLiterally()
    {
        string windows = ScheduledTaskPlan.Definition(Windows with { Home = @"C:\Mesh Home", Daemon = @"C:\Program Files\Axiom.Mesh\axiom-meshd.exe" });
        Assert.Contains(@"<Arguments>--headless &quot;C:\Program Files\Axiom.Mesh\axiom-meshd.exe&quot; run --home &quot;C:\Mesh Home&quot;</Arguments>", windows, StringComparison.Ordinal);

        string unit = SystemdUnitPlan.Unit(Linux with { Home = "/home/alice/mesh 100%$HOME", Daemon = "/opt/a\"b/axiom-meshd" });
        Assert.Contains("\nExecStart=\"/opt/a\\\"b/axiom-meshd\" \"run\" \"--home\" \"/home/alice/mesh 100%%$$HOME\"\n", unit, StringComparison.Ordinal);

        Assert.Equal(["run", "--home", @"C:\Mesh Home"], ScheduledTaskPlan.Install(Windows with { Home = @"C:\Mesh Home" }, start: true).DaemonArguments);
    }

    // AXIOM_MESH_PIPE in the installing shell does not reach a Host started at logon: the plan says so and names pipe.name.
    [Fact]
    public void APipeVariableInTheInstallingShellIsNamedInANote()
    {
        Assert.Empty(SystemdUnitPlan.Install(Linux, start: true).Notes);

        string note = Assert.Single(SystemdUnitPlan.Install(Linux with { PipeVariable = "/tmp/x.sock" }, start: true).Notes);
        Assert.Contains("AXIOM_MESH_PIPE", note, StringComparison.Ordinal);
        Assert.Contains("pipe.name", note, StringComparison.Ordinal);
    }

    // The XDG rule for the user unit directory: $XDG_CONFIG_HOME/systemd/user when absolute, else ~/.config/systemd/user.
    [Theory]
    [InlineData("/xdg", "/home/a", "/xdg/systemd/user")] // XDG_CONFIG_HOME absolute
    [InlineData("relative", "/home/a", "/home/a/.config/systemd/user")] // a relative XDG_CONFIG_HOME is ignored
    [InlineData(null, "/home/a", "/home/a/.config/systemd/user")] // no XDG_CONFIG_HOME
    [InlineData(null, null, null)] // neither: unknown
    public void TheUnitDirectoryFollowsTheXdgRule(string? xdg, string? home, string? expected)
    {
        string? directory = AutostartPlans.UnitDirectory(name => name switch { "XDG_CONFIG_HOME" => xdg, "HOME" => home, _ => null });

        Assert.Equal(expected, directory?.Replace('\\', '/'));
    }
}
