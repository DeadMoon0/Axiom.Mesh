using System;

namespace Axiom.Mesh.Service;

/// <summary>
/// What <see cref="ReconnectRule"/> decided: retry after a delay, or exit with a code. Only the two factories make one, so
/// a retry always has its delay and an exit its code.
/// </summary>
public sealed record ReconnectDecision
{
    private ReconnectDecision(ReconnectAction action, TimeSpan delay, ServiceExitCode exitCode)
    {
        Action = action;
        Delay = delay;
        ExitCode = exitCode;
    }

    /// <summary>Retry or exit.</summary>
    public ReconnectAction Action { get; }

    /// <summary>How long to wait before the retry; zero for an exit.</summary>
    public TimeSpan Delay { get; }

    /// <summary>What to exit with; <see cref="ServiceExitCode.Ok"/> for a retry.</summary>
    public ServiceExitCode ExitCode { get; }

    /// <summary>Connect again after a delay.</summary>
    /// <param name="delay">The delay.</param>
    /// <returns>The decision.</returns>
    public static ReconnectDecision Retry(TimeSpan delay) => new(ReconnectAction.Retry, delay, ServiceExitCode.Ok);

    /// <summary>Stop the process.</summary>
    /// <param name="exitCode">What to exit with.</param>
    /// <returns>The decision.</returns>
    public static ReconnectDecision Exit(ServiceExitCode exitCode) => new(ReconnectAction.Exit, TimeSpan.Zero, exitCode);

    /// <inheritdoc />
    public override string ToString() => Action == ReconnectAction.Retry ? $"retry in {Delay.TotalSeconds:0} s" : $"exit {ExitCode}";
}
