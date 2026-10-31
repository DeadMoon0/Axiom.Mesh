using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Axiom.Mesh.Cli;

/// <summary>
/// The Windows autostart (fundamentals plan §6e): a per-user scheduled task <see cref="TaskName"/> with a logon trigger for
/// the user's own account, run with the user's interactive token at least privilege (so DPAPI and the owner-only pipe
/// work), with no time limit and not stopped on batteries. The task starts the daemon through <c>conhost.exe
/// --headless</c>, so no console window opens at logon. Pure: the task definition and the <c>schtasks</c> calls are planned
/// from the target.
/// </summary>
internal static class ScheduledTaskPlan
{
    /// <summary>The task's name in the Task Scheduler library.</summary>
    public const string TaskName = @"\Axiom.Mesh\axiom-meshd";

    /// <summary>The program that registers, runs, ends and deletes tasks.</summary>
    public const string Schtasks = "schtasks";

    /// <summary>The file name of the task definition in the temporary directory.</summary>
    public const string DefinitionFile = "axiom-meshd.task.xml";

    /// <summary>
    /// Registers the task (replacing one of the same name) and, with <paramref name="start"/>, stops a running Host of
    /// that executable and starts the task now.
    /// </summary>
    /// <param name="target">The daemon, home and account.</param>
    /// <param name="start">Whether to start the Host now.</param>
    /// <returns>The plan.</returns>
    public static AutostartPlan Install(AutostartTarget target, bool start)
    {
        ArgumentNullException.ThrowIfNull(target);
        string definition = WindowsPath(target.TemporaryDirectory, DefinitionFile);
        var steps = new List<AutostartStep>
        {
            new WriteStep(definition, Definition(target), Utf16: true),
            new RunStep(Schtasks, ["/Create", "/TN", TaskName, "/XML", definition, "/F"], MayFail: false, Removes: false),
            new DeleteStep(definition, Removes: false),
        };
        if (start)
        {
            steps.Add(new RunStep(Schtasks, ["/End", "/TN", TaskName], MayFail: true, Removes: false));
            steps.Add(new StopStep(target.Daemon));
            steps.Add(new RunStep(Schtasks, ["/Run", "/TN", TaskName], MayFail: false, Removes: false));
        }

        return Plan(target, start, steps);
    }

    /// <summary>Stops the Host the task runs and deletes the task; a task that does not exist is no failure.</summary>
    /// <param name="target">The daemon, for stopping it.</param>
    /// <returns>The plan.</returns>
    public static AutostartPlan Uninstall(AutostartTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Plan(target, start: false,
        [
            new RunStep(Schtasks, ["/End", "/TN", TaskName], MayFail: true, Removes: false),
            new StopStep(target.Daemon),
            new RunStep(Schtasks, ["/Delete", "/TN", TaskName, "/F"], MayFail: true, Removes: true),
        ]);
    }

    /// <summary>The task definition, Task Scheduler schema 1.2.</summary>
    /// <param name="target">The daemon, home and account.</param>
    /// <returns>The XML text, declared UTF-16, CRLF line ends.</returns>
    public static string Definition(AutostartTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        string account = Xml(target.Account);
        string arguments = string.Join(' ', AutostartPlans.DaemonArguments(target).Select(Quote).Prepend(Quote(target.Daemon)).Prepend("--headless"));
        string[] lines =
        [
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>",
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">",
            "  <RegistrationInfo>",
            "    <Description>Starts the Axiom.Mesh Host (axiom-meshd) at logon. Written by axiom-mesh host install; removed by axiom-mesh host uninstall.</Description>",
            $"    <URI>{Xml(TaskName)}</URI>",
            "  </RegistrationInfo>",
            "  <Triggers>",
            $"    <LogonTrigger><Enabled>true</Enabled><UserId>{account}</UserId></LogonTrigger>",
            "  </Triggers>",
            "  <Principals>",
            $"    <Principal id=\"Author\"><UserId>{account}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal>",
            "  </Principals>",
            "  <Settings>",
            "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>",
            "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>",
            "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>",
            "    <AllowHardTerminate>true</AllowHardTerminate>",
            "    <StartWhenAvailable>false</StartWhenAvailable>",
            "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>",
            "    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>",
            "    <AllowStartOnDemand>true</AllowStartOnDemand>",
            "    <Enabled>true</Enabled>",
            "    <Hidden>false</Hidden>",
            "    <RunOnlyIfIdle>false</RunOnlyIfIdle>",
            "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>",
            "    <Priority>7</Priority>",
            "    <RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>",
            "  </Settings>",
            "  <Actions Context=\"Author\">",
            "    <Exec>",
            @"      <Command>%SystemRoot%\System32\conhost.exe</Command>",
            $"      <Arguments>{Xml(arguments)}</Arguments>",
            $"      <WorkingDirectory>{Xml(WindowsDirectoryOf(target.Daemon))}</WorkingDirectory>",
            "    </Exec>",
            "  </Actions>",
            "</Task>",
        ];
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static AutostartPlan Plan(AutostartTarget target, bool start, IReadOnlyList<AutostartStep> steps) => new()
    {
        System = AutostartSystem.ScheduledTask,
        Registration = TaskName,
        Daemon = target.Daemon,
        DaemonArguments = AutostartPlans.DaemonArguments(target),
        Starts = start,
        Steps = steps,
        Notes = AutostartPlans.Notes(target),
    };

    // A Windows command-line argument (CommandLineToArgvW): a word without spaces or backslashes as it is, anything else in
    // quotes without a trailing backslash, which would escape the closing quote. A Windows path never holds a quote.
    private static string Quote(string argument) =>
        argument.Length > 0 && argument.IndexOfAny([' ', '\\', '\t']) < 0 ? argument : "\"" + argument.TrimEnd('\\') + "\"";

    // Windows paths are joined and split by their own separator whatever platform plans them, so the plan is the same in
    // a test on Linux.
    private static string WindowsPath(string directory, string file) => directory.TrimEnd('\\') + "\\" + file;

    private static string WindowsDirectoryOf(string file) => file.LastIndexOf('\\') is var at and > 0 ? file[..at] : string.Empty;

    private static string Xml(string text) => new StringBuilder(text)
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;")
        .ToString();
}
