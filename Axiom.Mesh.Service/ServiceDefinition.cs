using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Axiom.Mesh.Service;

/// <summary>
/// Who a service is (LOCAL-SPEC.md §4.2, §9.1): its name as the Host's configuration lists it, its version for the Host's
/// logs, and the kinds it defines. The runtime registers every kind on every connection and catches up on their Entries.
/// </summary>
public sealed record ServiceDefinition
{
    private const int MaxNameBytes = 32;

    /// <summary>The service's name: 1 to 32 ASCII letters, digits, <c>.</c>, <c>-</c> and <c>_</c> (LOCAL-SPEC.md §3.2).</summary>
    public required string Name { get; init; }

    /// <summary>The service's version, sent as <c>Hello.clientVersion</c> and printed by <c>version</c>; never interpreted.</summary>
    public required string Version { get; init; }

    /// <summary>The Package kinds it defines with their Entry kinds; at least one.</summary>
    public required IReadOnlyList<ServiceKind> Kinds { get; init; }

    /// <summary>Every Entry kind of every Package kind, ascending: the filter of the service's Entry stream (§9.3).</summary>
    public IReadOnlyList<EntryKind> EntryKinds => [.. Kinds.SelectMany(k => k.EntryKinds).Distinct().Order()];

    /// <summary>What is wrong with the definition, one entry per problem; empty when the runtime can run it.</summary>
    /// <returns>The problems.</returns>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        int bytes = Encoding.UTF8.GetByteCount(Name ?? string.Empty);
        if (bytes is 0 or > MaxNameBytes || !Name!.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            problems.Add($"the name \"{Name}\" is not 1 to 32 ASCII letters, digits, '.', '-' and '_'");
        if (Kinds is null || Kinds.Count == 0)
            problems.Add("it defines no Package kind");
        else if (Kinds.Select(k => k.Kind).Distinct().Count() != Kinds.Count)
            problems.Add("it names a Package kind twice");
        foreach (ServiceKind kind in Kinds ?? [])
        {
            if (kind.EntryKinds is null || kind.EntryKinds.Count == 0 || !kind.EntryKinds.Zip(kind.EntryKinds.Skip(1)).All(p => (ushort)p.First < (ushort)p.Second))
                problems.Add($"Package kind {(ushort)kind.Kind} needs its Entry kinds ascending, unique and at least one");
        }

        return problems;
    }
}
