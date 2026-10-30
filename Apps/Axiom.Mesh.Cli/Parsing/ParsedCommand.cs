namespace Axiom.Mesh.Cli;

/// <summary>The arguments are a command.</summary>
/// <param name="Command">The command, with every value it needs parsed.</param>
internal sealed record ParsedCommand(Command Command) : ParseResult;
