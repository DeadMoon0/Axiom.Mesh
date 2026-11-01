using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Sync;

/// <summary>
/// One <c>Version</c> Entry of an Archive as the rules see it (PAYLOADS.md §2, SPEC.md §9.3, §9.5): its hash, its place in
/// the total order, the Entries it follows, its branch and the Tree it names.
/// </summary>
/// <param name="Hash">The Entry's hash.</param>
/// <param name="Key">Its place in the total order: clock, device, hash.</param>
/// <param name="Parents">The Entries it names as parents.</param>
/// <param name="Label">Its branch.</param>
/// <param name="Tree">The Tree it names.</param>
internal sealed record VersionFact(ObjectHash Hash, EntryKey Key, IReadOnlyList<ObjectHash> Parents, string Label, ObjectHash Tree)
{
    /// <summary>
    /// The Version an Entry is (PAYLOADS.md §2, §1.2): of kind Version, its payload parsing to its end, and its envelope's
    /// <c>references</c> naming exactly its Tree. Anything else is no Version this service follows (a writer's fault).
    /// </summary>
    /// <param name="entry">The Entry as the Host stored it.</param>
    /// <param name="fact">The Version, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for a Version.</returns>
    public static bool TryOf(Entry entry, [NotNullWhen(true)] out VersionFact? fact)
    {
        fact = null;
        if (entry is null || entry.Kind != EntryKind.Version || !EntryPayload.TryParse(entry.Kind, entry.Payload, out EntryPayload? payload) || payload is not VersionPayload version)
            return false;
        if (entry.References.Count != 1 || entry.References[0] != version.Tree)
            return false;
        fact = new VersionFact(entry.Hash, new EntryKey(entry.Clock, entry.DeviceId, entry.Hash), [.. entry.Parents], version.Label, version.Tree);
        return true;
    }
}
