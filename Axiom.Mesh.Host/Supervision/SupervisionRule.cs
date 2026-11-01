using System;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Host;

/// <summary>
/// What the daemon does when a service it spawned exits (LOCAL-SPEC.md §9.1, debt 32, plan §9.2 item 5): while the daemon
/// stops, nothing (stopped). Exit codes 2 (configuration) and 3 (refused) give up until the daemon restarts: starting the
/// same program again cannot help. An exit within 10 seconds of the start is a crash; five crashes within five minutes
/// give up as well. Anything else is restarted after the backoff of LOCAL-SPEC.md §2.3 over the crashes in a row; a run of
/// 60 seconds or more forgets the crashes before it. Giving up is never silent: the decision carries its reason.
/// </summary>
internal static class SupervisionRule
{
    /// <summary>An exit this soon after the start is a crash.</summary>
    public static readonly TimeSpan CrashWithin = TimeSpan.FromSeconds(10);

    /// <summary>A run this long forgets the crashes before it.</summary>
    public static readonly TimeSpan HealthyRun = TimeSpan.FromSeconds(60);

    /// <summary>The window crashes are counted in.</summary>
    public static readonly TimeSpan CrashWindow = TimeSpan.FromMinutes(5);

    /// <summary>The crashes in the window that give up.</summary>
    public const int MaxCrashes = 5;

    private const int ConfigurationExit = 2;
    private const int RefusedExit = 3;

    /// <summary>Decides.</summary>
    /// <param name="state">What the supervisor remembers.</param>
    /// <param name="exit">How the child ended.</param>
    /// <param name="now">When it ended.</param>
    /// <returns>The decision and the next state.</returns>
    public static SupervisionDecision Decide(SupervisionState state, ChildExit exit, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(exit);
        if (state.Stopping)
            return new SupervisionDecision(SupervisionAction.Stopped, TimeSpan.Zero, state, exit.Killed ? "killed after the stop grace" : "the daemon stops");
        if (exit.ExitCode is ConfigurationExit or RefusedExit)
        {
            string why = exit.ExitCode == ConfigurationExit ? "it cannot run as configured (exit code 2)" : "the daemon refused its name or token (exit code 3)";
            return new SupervisionDecision(SupervisionAction.GiveUp, TimeSpan.Zero, state, why + "; restart the daemon once the cause is fixed");
        }

        List<DateTimeOffset> crashes = exit.RanFor >= HealthyRun ? [] : [.. state.Crashes.Where(at => now - at < CrashWindow)];
        if (exit.RanFor < CrashWithin)
            crashes.Add(now);
        SupervisionState next = state with { Crashes = crashes };
        if (crashes.Count >= MaxCrashes)
            return new SupervisionDecision(SupervisionAction.GiveUp, TimeSpan.Zero, next, $"{crashes.Count} crashes within {CrashWindow.TotalMinutes:0} minutes; restart the daemon once the cause is fixed");

        TimeSpan delay = Reconnect.Delay(Math.Max(crashes.Count, 1));
        return new SupervisionDecision(SupervisionAction.Restart, delay, next, $"exit code {exit.ExitCode} after {exit.RanFor.TotalSeconds:0.#} s; restart in {delay.TotalSeconds:0} s");
    }
}
