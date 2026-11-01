using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>The services a Host lists (LOCAL-SPEC.md §5.12), as text and as JSON. Pure.</summary>
internal static class ServicesView
{
    /// <summary>The services as a table.</summary>
    /// <param name="services">The services.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Text(IReadOnlyList<Service> services) => TextTable.Lines(
        ["NAME", "PROVENANCE", "CONNECTED", "VERSION", "KINDS", "ENTRY KINDS"],
        services.Select(s => (IReadOnlyList<string>)[
            s.Name,
            ProvenanceOf(s),
            Formats.YesNo(s.Connected),
            s.Version.Length == 0 ? "-" : s.Version,
            Kinds(s.Kinds.Select(k => (ushort)k)),
            Kinds(s.EntryKinds.Select(k => (ushort)k))]),
        "no services");

    /// <summary>The services as JSON.</summary>
    /// <param name="services">The services.</param>
    /// <returns>The document.</returns>
    public static string Json(IReadOnlyList<Service> services) => JsonOut.Of(w =>
    {
        w.WriteStartArray();
        foreach (Service s in services)
        {
            w.WriteStartObject();
            w.WriteString("name", s.Name);
            w.WriteString("provenance", ProvenanceOf(s));
            w.WriteBoolean("connected", s.Connected);
            w.WriteString("version", s.Version);
            w.WriteStartArray("kinds");
            foreach (PackageKind kind in s.Kinds)
                w.WriteNumberValue((ushort)kind);
            w.WriteEndArray();
            w.WriteStartArray("entryKinds");
            foreach (EntryKind kind in s.EntryKinds)
                w.WriteNumberValue((ushort)kind);
            w.WriteEndArray();
            w.WriteEndObject();
        }

        w.WriteEndArray();
    });

    // §5.12: a service that is neither in process nor spawned is external.
    private static string ProvenanceOf(Service service) => service.InProcess ? "in-process" : service.Spawned ? "spawned" : "external";

    private static string Kinds(IEnumerable<ushort> kinds) => kinds.Any() ? string.Join(",", kinds) : "-";
}
