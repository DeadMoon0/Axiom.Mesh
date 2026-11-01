using System;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat;

/// <summary>
/// What the index does when the service connects (LOCAL-SPEC.md §9.3, CHAT-SERVICE.md §10): rows built under another store
/// epoch or another schema are dropped. The runtime replays from store sequence 1 when its own cursor was discarded or never
/// written; when it resumes further on (the index and the cursor disagree, a crash between their commits, a schema change),
/// the rows are rebuilt from each followed Channel's history instead, since no replay would bring them back. Pure.
/// </summary>
internal static class CatchUpPlan
{
    /// <summary>The schema version this build writes.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Decides.</summary>
    /// <param name="schemaVersion">The schema the rows were written with; 0 for an index without its meta row.</param>
    /// <param name="indexedEpoch">The store epoch the rows were built under; zero when none.</param>
    /// <param name="runtime">How the runtime catches up this session.</param>
    /// <param name="hostEpoch">The Host's store epoch.</param>
    /// <returns>Keep, Discard or DiscardAndRebuild.</returns>
    public static IndexAction Decide(int schemaVersion, Hash32 indexedEpoch, IndexCatchUp runtime, Hash32 hostEpoch)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        bool replaysAll = runtime.FromSeq <= 1;
        bool foreignSchema = schemaVersion != 0 && schemaVersion != SchemaVersion;
        bool foreignEpoch = !indexedEpoch.IsZero && indexedEpoch != hostEpoch;
        bool fresh = schemaVersion == 0;
        if (foreignSchema || foreignEpoch || runtime.DiscardIndex)
            return replaysAll ? IndexAction.Discard : IndexAction.DiscardAndRebuild;
        return fresh && !replaysAll ? IndexAction.DiscardAndRebuild : IndexAction.Keep;
    }
}
