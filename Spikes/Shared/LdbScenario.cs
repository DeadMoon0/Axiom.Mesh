using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Entity;
using Axiom.LDB.Schema;

namespace Axiom.Mesh.Spikes;

/// <summary>Open question 2 of the design, the LDB half: a file database in the platform's data directory, one write, one read.</summary>
public static class LdbScenario
{
    public static async Task<bool> RunAsync(string directory, SpikeLog log, CancellationToken cancellationToken)
    {
        log.Add($"--- LDB: open {directory} on {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})");
        try
        {
            await using Database db = await Database.OpenAsync(directory, DatabaseOptions.Default, cancellationToken).ConfigureAwait(false);
            await using (LdbContext context = await LdbContext.OpenAsync(db, cancellationToken).ConfigureAwait(false))
            {
                await context.EnsureTableAsync<Peer>(cancellationToken).ConfigureAwait(false);
                context.Set<Peer>().Add(new Peer { DeviceId = Guid.NewGuid().ToString("N"), Name = "spike", LastSeenUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                int written = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                log.Add($"[ldb] wrote {written} row(s)");
            }

            await using (LdbContext context = await LdbContext.OpenAsync(db, cancellationToken).ConfigureAwait(false))
            {
                long count = await context.Set<Peer>().Query().CountAsync(cancellationToken).ConfigureAwait(false);
                log.Add($"[ldb] {count} row(s) in Peers after reopening the context");
                return count >= 1;
            }
        }
        catch (Exception ex)
        {
            log.Add($"[ldb] FAILED: {ex}");
            return false;
        }
    }
}

[LdbEntity]
public sealed partial class Peer
{
    [Key, AutoIncrement]
    public long Id { get; set; }

    [Column(MaxLength = 64), Index(Unique = true)]
    public string DeviceId { get; set; } = "";

    public string? Name { get; set; }

    public long LastSeenUnixMs { get; set; }
}
