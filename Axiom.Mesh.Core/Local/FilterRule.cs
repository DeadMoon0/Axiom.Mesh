using System.Collections.Generic;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// What a <c>Subscribe</c> may ask for and what an event matches (LOCAL-SPEC.md §5.18, §7.1, §7.2): each topic reads
/// some fields of the filter, a field it does not read must be zero or empty, and an unknown topic is <c>Invalid</c>.
/// </summary>
internal static class FilterRule
{
    /// <summary>Checks a filter against its topic.</summary>
    /// <param name="topic">The topic.</param>
    /// <param name="filter">The filter.</param>
    /// <returns><see langword="null"/> when acceptable; else the field that is wrong, for <c>Error.detail</c>.</returns>
    public static string? Check(Topic topic, Filter filter)
    {
        if (topic is < Topic.Packages or > Topic.Store)
            return $"topic {(ushort)topic} is not one of LOCAL-SPEC.md §14.5";

        bool package = topic is Topic.Packages or Topic.Entries or Topic.Transfers or Topic.Service;
        bool device = topic is Topic.Peers or Topic.Transfers;
        bool user = topic is Topic.Users;
        bool kinds = topic is Topic.Packages or Topic.Entries;
        bool operation = topic is Topic.Operations;
        bool service = topic is Topic.Service;
        bool series = topic is Topic.Telemetry;
        bool fromSeq = topic is Topic.Entries;

        if (!package && !filter.PackageId.Value.IsZero)
            return "filter.packageId: the topic does not read it";
        if (!device && !filter.DeviceId.Value.IsZero)
            return "filter.deviceId: the topic does not read it";
        if (!user && !filter.UserId.Value.IsZero)
            return "filter.userId: the topic does not read it";
        if (!kinds && filter.Kinds.Count != 0)
            return "filter.kinds: the topic does not read it";
        if (!operation && filter.OperationId != 0)
            return "filter.operation: the topic does not read it";
        if (!service && filter.Service.Length != 0)
            return "filter.service: the topic does not read it";
        if (!series && (filter.Series.Length != 0 || filter.Key.Length != 0))
            return "filter.series or filter.key: the topic does not read it";
        if (!fromSeq && filter.FromSeq != 0)
            return "filter.fromSeq: the topic does not read it";
        if (!IsAscending(filter.Kinds))
            return "filter.kinds: not strictly ascending";
        return null;
    }

    /// <summary>Whether an event about a Package of kind <paramref name="kind"/> passes the filter's Package and kinds.</summary>
    /// <param name="filter">The filter.</param>
    /// <param name="package">The Package.</param>
    /// <param name="kind">The Package kind or Entry kind the topic filters by.</param>
    /// <returns><see langword="true"/> when it matches.</returns>
    public static bool Matches(Filter filter, PackageId package, ushort kind) =>
        (filter.PackageId.Value.IsZero || filter.PackageId == package) && (filter.Kinds.Count == 0 || Contains(filter.Kinds, kind));

    private static bool Contains(IReadOnlyList<ushort> kinds, ushort kind)
    {
        foreach (ushort candidate in kinds)
        {
            if (candidate == kind)
                return true;
        }

        return false;
    }

    private static bool IsAscending(IReadOnlyList<ushort> kinds)
    {
        for (int i = 1; i < kinds.Count; i++)
        {
            if (kinds[i] <= kinds[i - 1])
                return false;
        }

        return true;
    }
}
