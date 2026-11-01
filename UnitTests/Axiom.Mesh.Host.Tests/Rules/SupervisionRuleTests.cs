using System;
using System.Linq;

namespace Axiom.Mesh.Host.Tests.Rules;

/// <summary>
/// What the daemon does when a service it spawned exits (LOCAL-SPEC.md §9.1, plan §9.2 item 5): restart with the backoff of
/// §2.3; give up, loudly, after five crashes (exits within 10 seconds) in five minutes or on exit code 2 or 3; forget the
/// crashes after a run of a minute; and nothing while the daemon stops.
/// </summary>
public sealed class SupervisionRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    // A crash is restarted after 1 s, the next after 2 s, then 4 s, 8 s: the backoff of §2.3 over the crashes in a row.
    [Fact]
    public void CrashesAreRestartedWithTheBackoffOfSection2Point3()
    {
        SupervisionState state = SupervisionState.Fresh;
        var delays = new TimeSpan[4];
        for (int i = 0; i < delays.Length; i++)
        {
            SupervisionDecision decision = SupervisionRule.Decide(state, new ChildExit(1, TimeSpan.FromSeconds(2), false), Now.AddSeconds(i * 10));
            Assert.Equal(SupervisionAction.Restart, decision.Action);
            delays[i] = decision.Delay;
            state = decision.Next;
        }

        Assert.Equal([1, 2, 4, 8], delays.Select(d => (int)d.TotalSeconds));
    }

    // The fifth crash within five minutes gives up until the daemon restarts, saying why; crashes older than five minutes
    // do not count.
    [Fact]
    public void FiveCrashesWithinFiveMinutesGiveUp_OlderOnesDoNotCount()
    {
        SupervisionState old = SupervisionState.Fresh with { Crashes = [Now.AddMinutes(-6), Now.AddMinutes(-4), Now.AddMinutes(-3), Now.AddMinutes(-2)] };
        SupervisionState recent = SupervisionState.Fresh with { Crashes = [Now.AddMinutes(-4), Now.AddMinutes(-3), Now.AddMinutes(-2), Now.AddMinutes(-1)] };

        SupervisionDecision fourth = SupervisionRule.Decide(old, new ChildExit(134, TimeSpan.FromSeconds(1), false), Now);
        SupervisionDecision fifth = SupervisionRule.Decide(recent, new ChildExit(134, TimeSpan.FromSeconds(1), false), Now);

        Assert.Equal(SupervisionAction.Restart, fourth.Action);
        Assert.Equal(SupervisionAction.GiveUp, fifth.Action);
        Assert.Contains("5 crashes within 5 minutes", fifth.Why, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2, true)] // Configuration: the same program cannot do better
    [InlineData(3, true)] // Refused: the daemon refused its name or token
    [InlineData(0, false)] // it ended by itself: it is meant to run
    [InlineData(4, false)] // State: a failure that a restart may cure
    public void ExitCodes2And3GiveUpAtOnce_AnyOtherIsRestarted(int code, bool givesUp)
    {
        SupervisionDecision decision = SupervisionRule.Decide(SupervisionState.Fresh, new ChildExit(code, TimeSpan.FromMinutes(2), false), Now);

        Assert.Equal(givesUp ? SupervisionAction.GiveUp : SupervisionAction.Restart, decision.Action);
    }

    // A run of a minute or more forgets the crashes before it: the next restart waits 1 s again.
    [Fact]
    public void ARunOfAMinuteForgetsTheCrashesBeforeIt()
    {
        SupervisionState crashed = SupervisionState.Fresh with { Crashes = [Now.AddSeconds(-90), Now.AddSeconds(-80), Now.AddSeconds(-70), Now.AddSeconds(-61)] };

        SupervisionDecision decision = SupervisionRule.Decide(crashed, new ChildExit(1, TimeSpan.FromSeconds(60), false), Now);

        Assert.Equal((SupervisionAction.Restart, TimeSpan.FromSeconds(1)), (decision.Action, decision.Delay));
        Assert.Empty(decision.Next.Crashes);
    }

    // While the daemon stops, an exit is no failure, killed or not: nothing is restarted and nothing given up.
    [Fact]
    public void WhileTheDaemonStopsAnExitIsStopped_KilledOrNot()
    {
        SupervisionState stopping = SupervisionState.Fresh with { Stopping = true };

        SupervisionDecision drained = SupervisionRule.Decide(stopping, new ChildExit(0, TimeSpan.FromSeconds(1), false), Now);
        SupervisionDecision killed = SupervisionRule.Decide(stopping, new ChildExit(-1, TimeSpan.FromSeconds(1), true), Now);

        Assert.Equal((SupervisionAction.Stopped, SupervisionAction.Stopped), (drained.Action, killed.Action));
        Assert.Empty(drained.Next.Crashes);
    }
}
