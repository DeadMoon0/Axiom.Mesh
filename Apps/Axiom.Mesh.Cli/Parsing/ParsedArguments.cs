using System.Collections.Generic;

namespace Axiom.Mesh.Cli;

/// <summary>The words of a command line after its command's name, sorted into positionals, option values and flags.</summary>
internal sealed class ParsedArguments
{
    private readonly IReadOnlyList<string> positionals;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> values;
    private readonly IReadOnlySet<string> flags;

    /// <summary>Parsed arguments.</summary>
    /// <param name="spec">The command they belong to.</param>
    /// <param name="positionals">The positional arguments, in order.</param>
    /// <param name="values">The options given with a value, each value in the order given.</param>
    /// <param name="flags">The flags given.</param>
    public ParsedArguments(CommandSpec spec, IReadOnlyList<string> positionals, IReadOnlyDictionary<string, IReadOnlyList<string>> values, IReadOnlySet<string> flags)
    {
        Spec = spec;
        this.positionals = positionals;
        this.values = values;
        this.flags = flags;
    }

    /// <summary>The command they belong to.</summary>
    public CommandSpec Spec { get; }

    /// <summary>Every positional argument, in order.</summary>
    public IReadOnlyList<string> Positionals => positionals;

    /// <summary>The positional argument at an index, which the spec requires.</summary>
    /// <param name="index">The index.</param>
    /// <returns>Its text.</returns>
    public string this[int index] => positionals[index];

    /// <summary>An optional positional argument.</summary>
    /// <param name="index">The index.</param>
    /// <returns>Its text, or <see langword="null"/> when it was left out.</returns>
    public string? Optional(int index) => index < positionals.Count ? positionals[index] : null;

    /// <summary>The value of an option.</summary>
    /// <param name="option">The option with its dashes.</param>
    /// <returns>The value, or <see langword="null"/> when it was not given.</returns>
    public string? Value(string option) => values.TryGetValue(option, out IReadOnlyList<string>? given) ? given[^1] : null;

    /// <summary>Every value of an option that may be given more than once, in order.</summary>
    /// <param name="option">The option with its dashes.</param>
    /// <returns>The values; empty when it was not given.</returns>
    public IReadOnlyList<string> Values(string option) => values.TryGetValue(option, out IReadOnlyList<string>? given) ? given : [];

    /// <summary>Whether a flag was given.</summary>
    /// <param name="flag">The flag with its dashes.</param>
    /// <returns><see langword="true"/> when it was.</returns>
    public bool Flag(string flag) => flags.Contains(flag);

    /// <summary>A usage error about this command.</summary>
    /// <param name="problem">What is wrong.</param>
    /// <returns>The error.</returns>
    public UsageError Problem(string problem) => new(problem, Spec);
}
