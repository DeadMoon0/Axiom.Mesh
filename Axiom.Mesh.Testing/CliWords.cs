using System;
using System.Collections.Generic;
using System.Text;

namespace Axiom.Mesh.Testing;

/// <summary>
/// Splits a command line into arguments as a POSIX shell would for the simple cases a test writes: words separated by
/// spaces, a double- or single-quoted word kept whole with its spaces, a backslash escaping the next character inside
/// double quotes and outside quotes.
/// </summary>
public static class CliWords
{
    /// <summary>The arguments of a command line.</summary>
    /// <param name="commandLine">The line, without the program's name.</param>
    /// <returns>The arguments.</returns>
    /// <exception cref="ArgumentException">A quote is not closed.</exception>
    public static IReadOnlyList<string> Split(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        var words = new List<string>();
        var word = new StringBuilder();
        bool inWord = false;
        char quote = '\0';
        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];
            if (quote == '\'')
            {
                if (c == '\'')
                    quote = '\0';
                else
                    word.Append(c);
                continue;
            }

            if (c == '\\' && i + 1 < commandLine.Length)
            {
                word.Append(commandLine[++i]);
                inWord = true;
                continue;
            }

            if (quote == '"')
            {
                if (c == '"')
                    quote = '\0';
                else
                    word.Append(c);
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                inWord = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inWord)
                    words.Add(word.ToString());
                word.Clear();
                inWord = false;
            }
            else
            {
                word.Append(c);
                inWord = true;
            }
        }

        if (quote != '\0')
            throw new ArgumentException($"The quote {quote} in '{commandLine}' is not closed.", nameof(commandLine));
        if (inWord)
            words.Add(word.ToString());
        return words;
    }
}
