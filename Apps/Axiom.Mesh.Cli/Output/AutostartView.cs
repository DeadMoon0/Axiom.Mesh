using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Axiom.Mesh.Cli;

/// <summary>What <c>host install</c> and <c>host uninstall</c> did, as text and as JSON. Pure.</summary>
internal static class AutostartView
{
    /// <summary>An install, as text.</summary>
    /// <param name="outcome">What the plan did.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> InstallText(AutostartOutcome outcome)
    {
        AutostartPlan plan = outcome.Plan;
        var lines = new List<string>
        {
            $"axiom-meshd starts at logon: {What(plan.System)} {plan.Registration}",
            $"  runs     {CommandLine(plan)}",
            plan.Starts ? "  started  now; check it with 'axiom-mesh status'" : "  started  no (--no-start); it starts at the next logon",
        };
        lines.AddRange(plan.Notes.Select(n => "note: " + n));
        return lines;
    }

    /// <summary>An uninstall, as text.</summary>
    /// <param name="outcome">What the plan did.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> UninstallText(AutostartOutcome outcome)
    {
        AutostartPlan plan = outcome.Plan;
        var lines = new List<string>
        {
            outcome.Removed
                ? $"axiom-meshd no longer starts at logon: {What(plan.System)} {plan.Registration} removed"
                : $"nothing to remove: no {What(plan.System)} {plan.Registration} was registered",
        };
        if (outcome.Stopped > 0)
            lines.Add($"  stopped  the running Host ({outcome.Stopped} process{(outcome.Stopped == 1 ? string.Empty : "es")})");
        return lines;
    }

    /// <summary>An install or uninstall, as JSON: the registration, the daemon's command line and every step's result.</summary>
    /// <param name="outcome">What the plan did.</param>
    /// <param name="install">Whether it was an install.</param>
    /// <returns>The document.</returns>
    public static string Json(AutostartOutcome outcome, bool install) => JsonOut.Of(w =>
    {
        AutostartPlan plan = outcome.Plan;
        w.WriteStartObject();
        w.WriteString("system", plan.System == AutostartSystem.ScheduledTask ? "scheduled-task" : "systemd-user");
        w.WriteString("registration", plan.Registration);
        w.WriteString("daemon", plan.Daemon);
        w.WriteStartArray("arguments");
        foreach (string argument in plan.DaemonArguments)
            w.WriteStringValue(argument);
        w.WriteEndArray();
        if (install)
            w.WriteBoolean("started", plan.Starts);
        else
            w.WriteBoolean("removed", outcome.Removed);
        w.WriteNumber("stopped", outcome.Stopped);
        w.WriteStartArray("steps");
        foreach (StepOutcome step in outcome.Steps)
            Step(w, step);
        w.WriteEndArray();
        w.WriteStartArray("notes");
        foreach (string note in plan.Notes)
            w.WriteStringValue(note);
        w.WriteEndArray();
        w.WriteEndObject();
    });

    private static void Step(Utf8JsonWriter w, StepOutcome step)
    {
        w.WriteStartObject();
        w.WriteString("step", step.Step.Describe());
        w.WriteNumber("exitCode", step.ExitCode);
        w.WriteString("output", step.Output);
        w.WriteEndObject();
    }

    private static string What(AutostartSystem system) => system == AutostartSystem.ScheduledTask ? "scheduled task" : "systemd user unit";

    private static string CommandLine(AutostartPlan plan) =>
        string.Join(' ', plan.DaemonArguments.Prepend(plan.Daemon).Select(a => a.Contains(' ', System.StringComparison.Ordinal) ? $"\"{a}\"" : a));
}
