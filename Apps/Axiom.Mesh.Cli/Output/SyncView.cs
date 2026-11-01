using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Axiom.Mesh.Sync;

namespace Axiom.Mesh.Cli;

/// <summary>
/// The sync namespace's records as the command line shows them (SYNC-SERVICE.md §4): bindings as a table, one binding in
/// detail, conflicts, and the lines <c>sync watch</c> prints, as text and as JSON. States and kinds are lower-case words, ids
/// are cut to 12 hex digits in text and whole in JSON. Pure.
/// </summary>
internal static class SyncView
{
    /// <summary>The bindings as a table.</summary>
    /// <param name="bindings">The bindings.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Bindings(IReadOnlyList<Binding> bindings) => TextTable.Lines(
        ["DIRECTORY", "ARCHIVE", "BRANCH", "STATE", "FILES", "SIZE", "LEAVES", "CONFLICTS", "LAST RUN"],
        bindings.Select(b => (IReadOnlyList<string>)[
            b.Directory,
            Formats.Short(b.PackageId.Value),
            b.Label,
            State(b),
            b.Files.ToString(CultureInfo.InvariantCulture),
            Formats.Bytes(b.Bytes),
            b.Leaves.ToString(CultureInfo.InvariantCulture),
            b.Conflicts.ToString(CultureInfo.InvariantCulture),
            Formats.Time(b.LastRun)]),
        "no directory is kept in sync; 'axiom-mesh sync add <dir> --create <name>' starts one");

    /// <summary>The bindings as JSON.</summary>
    /// <param name="bindings">The bindings.</param>
    /// <returns>The document.</returns>
    public static string BindingsJson(IReadOnlyList<Binding> bindings) => JsonOut.Of(w =>
    {
        w.WriteStartArray();
        foreach (Binding b in bindings)
            WriteBinding(w, b);
        w.WriteEndArray();
    });

    /// <summary>One binding in detail.</summary>
    /// <param name="binding">The binding.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Status(Binding binding)
    {
        var pairs = new List<(string, string)>
        {
            ("Directory", binding.Directory),
            ("Archive", binding.PackageId.Value.ToHex()),
            ("Branch", binding.Label),
            ("State", State(binding with { Pending = false }) + (binding.Rights < GrantRights.Write ? " (read-only)" : string.Empty) + (binding.Pending ? ", run queued" : string.Empty)),
            ("Files", $"{binding.Files.ToString(CultureInfo.InvariantCulture)}, {Formats.Bytes(binding.Bytes)}"),
            ("Head", Formats.Short(binding.Head.Value)),
            ("Leaves", binding.Leaves.ToString(CultureInfo.InvariantCulture)),
            ("Local changes", binding.LocalChanges.ToString(CultureInfo.InvariantCulture)),
            ("Conflicts", binding.Conflicts.ToString(CultureInfo.InvariantCulture)),
            ("Runs", binding.Runs.ToString(CultureInfo.InvariantCulture)),
            ("Last run", Formats.Time(binding.LastRun)),
        };
        if (binding.Ignore.Count > 0)
            pairs.Add(("Ignored", string.Join(", ", binding.Ignore)));
        if (binding.Error.Length > 0)
            pairs.Add(("Problem", binding.Error));
        return TextTable.Pairs(pairs);
    }

    /// <summary>The conflicts as a table.</summary>
    /// <param name="conflicts">The conflicts.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Conflicts(IReadOnlyList<Conflict> conflicts) => TextTable.Lines(
        ["ID", "AT", "KIND", "PATH", "COPY", "OTHER DEVICE"],
        conflicts.Select(c => (IReadOnlyList<string>)[
            c.Id.ToString(CultureInfo.InvariantCulture),
            Formats.Time(c.At),
            KindOf(c.Kind),
            c.Path,
            c.CopyPath.Length > 0 ? c.CopyPath : "-",
            Formats.Short(c.OtherDevice.Value)]),
        "no conflicts");

    /// <summary>The conflicts as JSON.</summary>
    /// <param name="conflicts">The conflicts.</param>
    /// <returns>The document.</returns>
    public static string ConflictsJson(IReadOnlyList<Conflict> conflicts) => JsonOut.Of(w =>
    {
        w.WriteStartArray();
        foreach (Conflict c in conflicts)
        {
            w.WriteStartObject();
            w.WriteNumber("id", c.Id);
            w.WriteString("directory", c.Directory);
            w.WriteString("path", c.Path);
            w.WriteString("kind", KindOf(c.Kind));
            w.WriteString("copyPath", c.CopyPath);
            JsonOut.Id(w, "otherDevice", c.OtherDevice.Value);
            JsonOut.Id(w, "version", c.Version.Value);
            w.WriteNumber("at", c.At);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    });

    /// <summary>The line <c>sync watch</c> prints for an event; <see langword="null"/> for one it does not show.</summary>
    /// <param name="sync">The event.</param>
    /// <param name="directory">Only this binding's events; <see langword="null"/> for every binding.</param>
    /// <returns>The line.</returns>
    public static string? WatchLine(SyncEvent sync, string? directory) => sync switch
    {
        BindingEvent { Binding: var b } when directory is null || b.Directory == directory =>
            $"{b.Directory}: {State(b)}, {b.Files.ToString(CultureInfo.InvariantCulture)} files, {b.Leaves.ToString(CultureInfo.InvariantCulture)} leaves, head {Formats.Short(b.Head.Value)}{(b.Error.Length > 0 ? ": " + b.Error : string.Empty)}",
        ConflictEvent { Conflict: var c } when directory is null || c.Directory == directory =>
            $"{c.Directory}: conflict ({KindOf(c.Kind)}) at {c.Path}{(c.CopyPath.Length > 0 ? ", the other side's copy is " + c.CopyPath : string.Empty)}",
        _ => null,
    };

    private static string KindOf(ConflictKind kind) => kind switch
    {
        ConflictKind.CopyWritten => "copy written",
        ConflictKind.NameRefused => "name refused",
        ConflictKind.TooLarge => "too large",
        ConflictKind.TooManyEntries => "too many entries",
        _ => Formats.Word(kind),
    };

    private static string State(Binding binding) =>
        binding.State switch
        {
            BindingState.InSync => "in sync",
            BindingState.LocalChangesReadOnly => "local changes kept",
            BindingState.WaitingForObjects => "waiting for files",
            _ => Formats.Word(binding.State),
        } + (binding.Pending ? ", run queued" : string.Empty);

    private static void WriteBinding(Utf8JsonWriter w, Binding b)
    {
        w.WriteStartObject();
        w.WriteString("directory", b.Directory);
        JsonOut.Id(w, "packageId", b.PackageId.Value);
        w.WriteString("label", b.Label);
        w.WriteString("symlinks", Formats.Word(b.Symlinks));
        w.WriteStartArray("ignore");
        foreach (string pattern in b.Ignore)
            w.WriteStringValue(pattern);
        w.WriteEndArray();
        w.WriteString("state", Formats.Word(b.State));
        w.WriteString("rights", Formats.Word(b.Rights));
        JsonOut.Id(w, "base", b.Base.Value);
        JsonOut.Id(w, "head", b.Head.Value);
        w.WriteNumber("leaves", b.Leaves);
        w.WriteNumber("files", b.Files);
        w.WriteNumber("bytes", b.Bytes);
        w.WriteNumber("localChanges", b.LocalChanges);
        w.WriteNumber("conflicts", b.Conflicts);
        w.WriteNumber("lastRun", b.LastRun);
        w.WriteNumber("runs", b.Runs);
        w.WriteBoolean("pending", b.Pending);
        w.WriteString("error", b.Error);
        w.WriteEndObject();
    }
}
