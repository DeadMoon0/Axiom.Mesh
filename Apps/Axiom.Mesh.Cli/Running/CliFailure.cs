using System;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// Why a command stops: the exit code and the one line that explains it. Raised inside a command, caught once by
/// <see cref="CliProgram"/>, which prints the line and never a stack trace.
/// </summary>
internal sealed class CliFailure : Exception
{
    private CliFailure(CliExitCode exitCode, string line)
        : base(line)
    {
        ExitCode = exitCode;
        Line = line;
    }

    /// <summary>What the process exits with.</summary>
    public CliExitCode ExitCode { get; }

    /// <summary>The one line printed to standard error.</summary>
    public string Line { get; }

    /// <summary>The Host refused a request (exit 3), explained by <see cref="StatusText"/>.</summary>
    /// <param name="status">The status.</param>
    /// <param name="error">The Host's error.</param>
    /// <param name="device">This device's name, for <c>UserKeyAbsent</c>.</param>
    /// <returns>The failure.</returns>
    public static CliFailure Refused(Status status, Error? error, string device) =>
        new(ExitCodes.Of(status), StatusText.Explain(status, error, device));

    /// <summary>The Host is not reachable (exit 2).</summary>
    /// <param name="address">Where it was expected.</param>
    /// <param name="why">What the connection attempt said; empty when nothing.</param>
    /// <returns>The failure.</returns>
    public static CliFailure Unreachable(string address, string why) =>
        new(CliExitCode.HostNotReachable, $"the Host is not reachable at {address}{(why.Length == 0 ? string.Empty : $" ({why})")}; start it with 'axiom-meshd run'");

    /// <summary>The Host ended or refused the connection itself (exit 2).</summary>
    /// <param name="why">What it said.</param>
    /// <returns>The failure.</returns>
    public static CliFailure Disconnected(string why) => new(CliExitCode.HostNotReachable, OneLine(why));

    /// <summary>The Host did not answer in time (exit 4).</summary>
    /// <param name="timeout">How long the command waited.</param>
    /// <returns>The failure.</returns>
    public static CliFailure TimedOut(TimeSpan timeout) =>
        new(CliExitCode.Timeout, $"the Host did not answer within {timeout.TotalSeconds:0} seconds; it may be busy, try again");

    /// <summary>Something this side cannot do or the Host would refuse anyway (exit 3), in one line.</summary>
    /// <param name="line">The explanation.</param>
    /// <returns>The failure.</returns>
    public static CliFailure Problem(string line) => new(CliExitCode.Refused, OneLine(line));

    /// <summary>A value of the command line that is wrong in substance (exit 1).</summary>
    /// <param name="line">The explanation.</param>
    /// <returns>The failure.</returns>
    public static CliFailure Usage(string line) => new(CliExitCode.Usage, OneLine(line));

    private static string OneLine(string text) => text.ReplaceLineEndings(" ").Trim();
}
