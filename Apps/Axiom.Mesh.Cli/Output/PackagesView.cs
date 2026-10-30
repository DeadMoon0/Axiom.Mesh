using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>Packages (LOCAL-SPEC.md §5.4) and their Grants (§5.5), as text and as JSON. Pure.</summary>
internal static class PackagesView
{
    /// <summary>The Packages as a table.</summary>
    /// <param name="packages">The Packages.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Text(IReadOnlyList<Package> packages) => TextTable.Lines(
        ["PACKAGE", "NAME", "KIND", "OWNER", "STATE", "RIGHTS", "ENTRIES"],
        packages.Select(p => (IReadOnlyList<string>)[
            Formats.Short(p.PackageId.Value),
            p.Name.Length == 0 ? "-" : p.Name,
            Formats.Word(p.Kind),
            Formats.Short(p.OwnerId.Value),
            Formats.Word(p.State),
            Rights(p.Rights),
            p.Entries.ToString(CultureInfo.InvariantCulture)]),
        "no packages");

    /// <summary>The Packages as JSON.</summary>
    /// <param name="packages">The Packages.</param>
    /// <returns>The document.</returns>
    public static string Json(IReadOnlyList<Package> packages) => JsonOut.Of(w =>
    {
        w.WriteStartArray();
        foreach (Package p in packages)
            Write(w, p);
        w.WriteEndArray();
    });

    /// <summary>One Package and its Grants as names and values, then a table of Grants.</summary>
    /// <param name="package">The Package.</param>
    /// <param name="grants">Its Grants.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> ShowText(Package package, IReadOnlyList<Grant> grants)
    {
        var lines = new List<string>(TextTable.Pairs(
        [
            ("Package", package.PackageId.Value.ToHex()),
            ("Name", package.Name),
            ("Kind", Formats.Word(package.Kind)),
            ("Owner", package.OwnerId.Value.ToHex()),
            ("State", Formats.Word(package.State)),
            ("Rights", Rights(package.Rights)),
            ("Created", Formats.Time(package.Created)),
            ("Retention", Formats.Word(package.Retention)),
            ("Policy", Formats.Word(package.Policy)),
            ("Entries", package.Entries.ToString(CultureInfo.InvariantCulture)),
            ("Heads", package.Heads.Count.ToString(CultureInfo.InvariantCulture)),
            ("Listed by", package.ListedBy.ToString(CultureInfo.InvariantCulture)),
        ]));
        lines.Add(string.Empty);
        lines.AddRange(TextTable.Lines(
            ["GRANTEE", "RIGHTS", "SEQ"],
            grants.Select(g => (IReadOnlyList<string>)[Formats.Short(g.GranteeId.Value), Rights(g.Rights), g.Seq.ToString(CultureInfo.InvariantCulture)]),
            "no grants"));
        return lines;
    }

    /// <summary>One Package and its Grants as JSON.</summary>
    /// <param name="package">The Package.</param>
    /// <param name="grants">Its Grants.</param>
    /// <returns>The document.</returns>
    public static string ShowJson(Package package, IReadOnlyList<Grant> grants) => JsonOut.Of(w =>
    {
        w.WriteStartObject();
        w.WritePropertyName("package");
        Write(w, package);
        w.WriteStartArray("grants");
        foreach (Grant g in grants)
        {
            w.WriteStartObject();
            JsonOut.Id(w, "hash", g.Hash.Value);
            JsonOut.Id(w, "granteeId", g.GranteeId.Value);
            w.WriteString("rights", Rights(g.Rights));
            w.WriteNumber("seq", g.Seq);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    });

    /// <summary>Rights as a word: <c>read</c>, <c>write</c>, <c>none</c>.</summary>
    /// <param name="rights">The rights.</param>
    /// <returns>The word.</returns>
    public static string Rights(GrantRights rights) => Formats.Word(rights);

    private static void Write(Utf8JsonWriter w, Package p)
    {
        w.WriteStartObject();
        JsonOut.Id(w, "packageId", p.PackageId.Value);
        w.WriteString("name", p.Name);
        w.WriteString("kind", Formats.Word(p.Kind));
        JsonOut.Id(w, "ownerId", p.OwnerId.Value);
        w.WriteString("state", Formats.Word(p.State));
        w.WriteString("rights", Rights(p.Rights));
        w.WriteNumber("created", p.Created);
        w.WriteString("retention", Formats.Word(p.Retention));
        w.WriteString("policy", Formats.Word(p.Policy));
        w.WriteNumber("manifestSeq", p.ManifestSeq);
        JsonOut.Id(w, "manifestHash", p.ManifestHash.Value);
        w.WriteNumber("listedBy", p.ListedBy);
        w.WriteNumber("entries", p.Entries);
        w.WriteNumber("latestClock", p.LatestClock);
        w.WriteNumber("heads", p.Heads.Count);
        w.WriteEndObject();
    }
}
