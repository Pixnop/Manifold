namespace Manifold.Internal;

/// <summary>A player whose surrounding columns should be streamed.</summary>
/// <param name="PlayerUid">Player unique id (for force-send).</param>
/// <param name="DimId">Engine dimension id the player is in.</param>
/// <param name="ChunkX">Player chunk X.</param>
/// <param name="ChunkZ">Player chunk Z.</param>
/// <param name="LoadRadius">Chunk radius to keep generated around the player.</param>
internal readonly record struct StreamingPlayer(string PlayerUid, int DimId, int ChunkX, int ChunkZ, int LoadRadius);
