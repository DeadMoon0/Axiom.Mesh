using System.Globalization;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Cli;

/// <summary>
/// What one line says about an Entry: a Message's text, a Version's label and Tree (PAYLOADS.md), the Entry a redaction
/// withdraws (its envelope's <c>redacts</c>, whatever its kind). A redacted Entry is shown as withdrawn and never with its text (SPEC.md §9.4). Pure.
/// </summary>
internal static class EntrySummary
{
    /// <summary>The longest text a summary shows of a message.</summary>
    public const int MaxText = 80;

    /// <summary>The line shown for a withdrawn Entry.</summary>
    public const string Withdrawn = "(withdrawn)";

    /// <summary>The summary of an Entry.</summary>
    /// <param name="entry">The Entry.</param>
    /// <returns>One line.</returns>
    public static string Of(Entry entry)
    {
        if (entry.Redacted)
            return Withdrawn;
        if (!entry.Redacts.IsZero)
            return $"withdraws {Formats.Short(entry.Redacts.Value)}";
        if (!EntryPayload.TryParse(entry.Kind, entry.Payload, out EntryPayload? payload))
            return $"{entry.Payload.Length.ToString(CultureInfo.InvariantCulture)} bytes";
        return payload switch
        {
            MessagePayload message => Cut(message.Text) + (message.Attachments.Count == 0 ? string.Empty : $" [{message.Attachments.Count.ToString(CultureInfo.InvariantCulture)} attachments]"),
            VersionPayload version => $"version {version.Label}{(version.Prerelease.Length == 0 ? string.Empty : "-" + version.Prerelease)}, tree {Formats.Short(version.Tree.Value)}",
            _ => $"{entry.Payload.Length.ToString(CultureInfo.InvariantCulture)} bytes",
        };
    }

    /// <summary>The Entry's kind as a word.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns><c>message</c>, <c>version</c>, <c>redaction</c>, or the number.</returns>
    public static string Kind(EntryKind kind) => Formats.Word(kind);

    private static string Cut(string text)
    {
        string line = text.ReplaceLineEndings(" ");
        return line.Length <= MaxText ? line : line[..(MaxText - 3)] + "...";
    }
}
