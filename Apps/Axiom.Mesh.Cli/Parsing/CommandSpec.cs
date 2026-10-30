using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Cli;

/// <summary>
/// One command of the command line as data: the words that name it, its arguments and options, one line of help, and
/// how its parsed arguments become the command. <see cref="CommandSpecs"/> holds every one.
/// </summary>
/// <param name="Words">The words that name it: <c>package create</c>.</param>
/// <param name="Arguments">Its positional arguments, in order.</param>
/// <param name="Options">Its options.</param>
/// <param name="Summary">One line about what it does.</param>
/// <param name="Build">Makes the command of parsed arguments, or says what is wrong with their values.</param>
internal sealed record CommandSpec(
    IReadOnlyList<string> Words,
    IReadOnlyList<ArgumentSpec> Arguments,
    IReadOnlyList<OptionSpec> Options,
    string Summary,
    Func<ParsedArguments, ParseResult> Build)
{
    /// <summary>The command's name: its words with spaces.</summary>
    public string Name => string.Join(' ', Words);

    /// <summary>The usage line: <c>axiom-mesh package create &lt;name&gt; [--kind &lt;kind&gt;]</c>.</summary>
    public string Usage => string.Join(' ', new[] { CliProgram.CommandName, Name }
        .Concat(Arguments.Select(a => a.Usage))
        .Concat(Options.Select(o => o.Usage)));

    /// <summary>The help text: the usage, the summary, and a line per option.</summary>
    /// <returns>The lines.</returns>
    public IEnumerable<string> HelpLines()
    {
        yield return "usage: " + Usage;
        yield return string.Empty;
        yield return "  " + Summary;
        if (Options.Count == 0)
            yield break;
        yield return string.Empty;
        int width = Options.Max(o => Form(o).Length);
        foreach (OptionSpec option in Options)
            yield return $"  {Form(option).PadRight(width)}  {option.Help}";
    }

    private static string Form(OptionSpec option) => option.Value is null ? option.Name : $"{option.Name} <{option.Value}>";
}
