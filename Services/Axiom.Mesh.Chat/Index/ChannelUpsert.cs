namespace Axiom.Mesh.Chat;

/// <summary>What <see cref="ChatIndex.UpsertChannelAsync"/> changed about a Channel.</summary>
/// <param name="Row">The Channel's row after it.</param>
/// <param name="Created">The row is new.</param>
/// <param name="Renamed">The name changed.</param>
/// <param name="FollowChanged">Whether it is followed changed.</param>
internal sealed record ChannelUpsert(ChannelRow Row, bool Created, bool Renamed, bool FollowChanged);
