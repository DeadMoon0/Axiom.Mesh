using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// Entries (LOCAL-SPEC.md §5.6): a Package's history, one Entry in full, and the lines <c>watch</c> prints, as text and
/// as JSON. A withdrawn Entry never shows its text, in JSON neither. Pure.
/// </summary>
internal static class HistoryView
{
    /// <summary>A history as a table.</summary>
    /// <param name="entries">The Entries, in the order shown.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Text(IReadOnlyList<Entry> entries) => TextTable.Lines(
        ["ENTRY", "CLOCK", "DEVICE", "KIND", "STORED", "SUMMARY"],
        entries.Select(e => (IReadOnlyList<string>)[
            Formats.Short(e.Hash.Value),
            e.Clock.ToString(CultureInfo.InvariantCulture),
            Formats.Short(e.DeviceId.Value),
            EntrySummary.Kind(e.Kind),
            Formats.Time(e.Stored),
            EntrySummary.Of(e)]),
        "no entries");

    /// <summary>A history as JSON.</summary>
    /// <param name="entries">The Entries.</param>
    /// <returns>The document.</returns>
    public static string Json(IReadOnlyList<Entry> entries) => JsonOut.Of(w =>
    {
        w.WriteStartArray();
        foreach (Entry e in entries)
            Write(w, e);
        w.WriteEndArray();
    });

    /// <summary>One Entry as names and values.</summary>
    /// <param name="entry">The Entry.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> ShowText(Entry entry) => TextTable.Pairs(
    [
        ("Entry", entry.Hash.Value.ToHex()),
        ("Package", entry.PackageId.Value.ToHex()),
        ("Device", entry.DeviceId.Value.ToHex()),
        ("Clock", entry.Clock.ToString(CultureInfo.InvariantCulture)),
        ("Parents", entry.Parents.Count == 0 ? "-" : string.Join(" ", entry.Parents.Select(p => p.Value.ToHex()))),
        ("Kind", EntrySummary.Kind(entry.Kind)),
        ("Stored", $"{Formats.Time(entry.Stored)} (store seq {entry.StoreSeq.ToString(CultureInfo.InvariantCulture)})"),
        ("Payload", EntrySummary.Of(entry)),
    ]);

    /// <summary>One Entry as JSON.</summary>
    /// <param name="entry">The Entry.</param>
    /// <returns>The document.</returns>
    public static string ShowJson(Entry entry) => JsonOut.Of(w => Write(w, entry));

    /// <summary>The line <c>watch</c> prints for an Entry stored.</summary>
    /// <param name="entry">The Entry.</param>
    /// <returns>One line.</returns>
    public static string WatchLine(Entry entry) =>
        $"{Formats.Time(entry.Stored)}  {Formats.Short(entry.PackageId.Value)}  {Formats.Short(entry.Hash.Value)}  {Formats.Short(entry.DeviceId.Value)}  {EntrySummary.Kind(entry.Kind)}  {EntrySummary.Of(entry)}";

    private static void Write(Utf8JsonWriter w, Entry e)
    {
        w.WriteStartObject();
        JsonOut.Id(w, "hash", e.Hash.Value);
        w.WriteNumber("storeSeq", e.StoreSeq);
        w.WriteNumber("stored", e.Stored);
        JsonOut.Id(w, "packageId", e.PackageId.Value);
        JsonOut.Id(w, "deviceId", e.DeviceId.Value);
        w.WriteNumber("clock", e.Clock);
        w.WriteStartArray("parents");
        foreach (ObjectHash parent in e.Parents)
            w.WriteStringValue(parent.Value.ToHex());
        w.WriteEndArray();
        w.WriteString("kind", EntrySummary.Kind(e.Kind));
        w.WriteBoolean("redacted", e.Redacted);
        w.WriteString("summary", EntrySummary.Of(e));
        w.WriteEndObject();
    }
}
