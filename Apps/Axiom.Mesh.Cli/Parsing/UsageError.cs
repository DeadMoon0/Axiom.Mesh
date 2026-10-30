namespace Axiom.Mesh.Cli;

/// <summary>The arguments are not a command: the problem in one line and the command it concerns, if one was recognised.</summary>
/// <param name="Problem">What is wrong, such as <c>missing &lt;package&gt;</c>.</param>
/// <param name="Spec">The command recognised; <see langword="null"/> when none was.</param>
internal sealed record UsageError(string Problem, CommandSpec? Spec) : ParseResult;
