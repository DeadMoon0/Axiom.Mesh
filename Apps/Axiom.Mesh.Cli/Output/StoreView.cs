using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>The store (LOCAL-SPEC.md §5.10) and its pins (§5.11), as text and as JSON. Pure.</summary>
internal static class StoreView
{
    /// <summary>The store as names and values.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Text(Store store) => TextTable.Pairs(
    [
        ("Path", store.Path.Length == 0 ? "(in memory)" : store.Path),
        ("Objects", store.Objects.ToString(CultureInfo.InvariantCulture)),
        ("Bytes", Formats.Bytes(store.Bytes)),
        ("Budget", store.Budget == 0 ? "none" : Formats.Bytes(store.Budget)),
        ("Pinned", Formats.Bytes(store.PinnedBytes)),
        ("Evictable", Formats.Bytes(store.EvictableBytes)),
        ("Held", Formats.Bytes(store.HeldBytes)),
    ]);

    /// <summary>The store as JSON.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The document.</returns>
    public static string Json(Store store) => JsonOut.Of(w =>
    {
        w.WriteStartObject();
        w.WriteString("path", store.Path);
        w.WriteNumber("objects", store.Objects);
        w.WriteNumber("bytes", store.Bytes);
        w.WriteNumber("budget", store.Budget);
        w.WriteNumber("pinnedBytes", store.PinnedBytes);
        w.WriteNumber("evictableBytes", store.EvictableBytes);
        w.WriteNumber("heldBytes", store.HeldBytes);
        w.WriteEndObject();
    });

    /// <summary>The pins as a table.</summary>
    /// <param name="pins">The pins.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> PinsText(IReadOnlyList<Pin> pins) => TextTable.Lines(
        ["OBJECT", "REASON", "SOURCE", "SIZE", "SINCE"],
        pins.Select(p => (IReadOnlyList<string>)[Formats.Short(p.Hash.Value), Formats.Word(p.Reason), Formats.Short(p.Source), Formats.Bytes(p.Size), Formats.Time(p.Since)]),
        "no pins");

    /// <summary>The pins as JSON.</summary>
    /// <param name="pins">The pins.</param>
    /// <returns>The document.</returns>
    public static string PinsJson(IReadOnlyList<Pin> pins) => JsonOut.Of(w =>
    {
        w.WriteStartArray();
        foreach (Pin p in pins)
        {
            w.WriteStartObject();
            JsonOut.Id(w, "hash", p.Hash.Value);
            w.WriteString("reason", Formats.Word(p.Reason));
            JsonOut.Id(w, "source", p.Source);
            w.WriteNumber("since", p.Since);
            w.WriteNumber("size", p.Size);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    });
}
