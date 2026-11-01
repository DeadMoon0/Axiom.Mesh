namespace Axiom.Mesh.Chat;

/// <summary>What the index's meta row says (<see cref="MetaRow"/>).</summary>
/// <param name="SchemaVersion">The schema the rows were written with; 0 when there is no meta row.</param>
/// <param name="StoreEpoch">The store epoch they were built under; zero when none.</param>
/// <param name="LastSeq">The store sequence of the last Entry taken in from the stream.</param>
internal sealed record IndexMeta(int SchemaVersion, Hash32 StoreEpoch, ulong LastSeq);
