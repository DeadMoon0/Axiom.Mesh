using System;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// How the command line writes an Entry of a kind (S-D5): through the service that holds the kind, with <c>ServiceCall</c>,
/// when that service is connected; else directly, as the external service <c>cli</c>. A kind is held by one connected
/// service at a time (LOCAL-SPEC.md §9.2), so the command line goes through the service whenever it runs, and keeps
/// working without it.
/// </summary>
internal static class WritePathRule
{
    /// <summary>The command line's own service name, which never counts as the holder.</summary>
    public const string Own = "cli";

    /// <summary>Decides.</summary>
    /// <param name="services">What <c>ListServices</c> answered.</param>
    /// <param name="kind">The Package kind written to.</param>
    /// <returns>Through the first connected service (by name) that may hold the kind, else directly.</returns>
    public static WritePath Decide(IReadOnlyList<Service> services, PackageKind kind)
    {
        ArgumentNullException.ThrowIfNull(services);
        Service? holder = services
            .Where(s => s.Connected && s.Name != Own && s.Kinds.Contains(kind))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        return holder is null ? WritePath.Direct : WritePath.ViaService(holder.Name);
    }
}
