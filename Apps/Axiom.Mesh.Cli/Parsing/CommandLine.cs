using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Cli;

/// <summary>
/// The command line's grammar, as a pure function (plan D9: no command-line library): the longest run of words that
/// names a command in <see cref="CommandSpecs"/>, then its positionals and options. <c>--help</c> anywhere asks for
/// the command's help; <c>--</c> ends the options.
/// </summary>
internal static class CommandLine
{
    /// <summary>Parses the arguments after the program's name.</summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The command, or what is wrong.</returns>
    public static ParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0)
            return new UsageError("no command given", null);
        if (args is ["--version"])
            return new ParsedCommand(new VersionCommand());
        if (args[0] is "--help" or "-h")
            return new ParsedCommand(new HelpCommand([.. args.Skip(1)]));

        CommandSpec? spec = CommandSpecs.Match(args);
        if (spec is null)
        {
            string named = string.Join(' ', args.TakeWhile(a => !a.StartsWith('-')).Take(2));
            return new UsageError(named.Length == 0 ? $"'{args[0]}' is not a command" : $"'{named}' is not a command", null);
        }

        return Arguments(spec, [.. args.Skip(spec.Words.Count)]);
    }

    private static ParseResult Arguments(CommandSpec spec, List<string> words)
    {
        var positionals = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        bool optionsEnded = false;
        for (int i = 0; i < words.Count; i++)
        {
            string word = words[i];
            if (optionsEnded || !word.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(word);
                continue;
            }

            if (word == "--")
            {
                optionsEnded = true;
                continue;
            }

            if (word == "--help")
                return new ParsedCommand(new HelpCommand(spec.Words));
            OptionSpec? option = spec.Options.FirstOrDefault(o => o.Name == word);
            if (option is null)
                return new UsageError($"{spec.Name} has no option {word}", spec);
            if (option.Value is null)
            {
                if (!flags.Add(word))
                    return new UsageError($"{word} is given twice", spec);
                continue;
            }

            if (i + 1 >= words.Count)
                return new UsageError($"{word} needs a value <{option.Value}>", spec);
            if (!values.TryAdd(word, words[++i]))
                return new UsageError($"{word} is given twice", spec);
        }

        int required = spec.Arguments.Count(a => a.Required);
        if (positionals.Count < required)
            return new UsageError("missing " + spec.Arguments[positionals.Count].Usage, spec);
        bool variadic = spec.Arguments.Count > 0 && spec.Arguments[^1].Variadic;
        if (positionals.Count > spec.Arguments.Count && !variadic)
            return new UsageError($"unexpected argument '{positionals[spec.Arguments.Count]}'", spec);
        if (spec.Options.FirstOrDefault(o => o.Required && !values.ContainsKey(o.Name)) is { } missing)
            return new UsageError($"missing {missing.Name} <{missing.Value}>", spec);

        return spec.Build(new ParsedArguments(spec, positionals, values, flags));
    }
}
