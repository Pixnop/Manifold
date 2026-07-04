using System;

namespace Manifold.Internal.Util;

/// <summary>
/// Chunk-coordinate helpers and the engine's chunk-size / per-dimension stride constants, kept in one
/// place so the bare literals (<c>32</c>, <c>1024</c>) are not duplicated across the codebase.
/// </summary>
internal static class ChunkMath
{
    /// <summary>Blocks per chunk edge (Vintage Story engine constant).</summary>
    public const int ChunkSize = 32;

    /// <summary>Chunk rows reserved per dimension in the engine's chunk-Y index (chunkY += dim * this).</summary>
    public const int DimensionChunkYStride = 1024;

    /// <summary>
    /// Maps a world coordinate to its chunk index using floor division, so negative coordinates map
    /// to the correct chunk (plain integer division truncates toward zero and is off by one there).
    /// </summary>
    /// <param name="world">World coordinate (block).</param>
    /// <returns>Chunk index.</returns>
    public static int ToChunk(double world) => (int)Math.Floor(world / ChunkSize);
}
