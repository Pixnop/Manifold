using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Manifold.Api.Worldgen;

/// <summary>Provided to <see cref="IWorldgenStrategy.GenerateColumn"/> for one chunk column.</summary>
/// <remarks>Server-side, main thread. Write blocks via <see cref="BlockAccessor"/> using dimension-encoded positions.</remarks>
public interface IWorldgenChunkContext
{
    /// <summary>Engine dimension id being generated.</summary>
    int DimensionId { get; }

    /// <summary>Chunk X index (multiply by 32 for world X).</summary>
    int ChunkX { get; }

    /// <summary>Chunk Z index (multiply by 32 for world Z).</summary>
    int ChunkZ { get; }

    /// <summary>
    /// Block accessor for writing terrain. Positions MUST be dimension-encoded
    /// (use <c>new BlockPos(x, y, z, DimensionId)</c>).
    /// </summary>
    IBlockAccessor BlockAccessor { get; }

    /// <summary>Deterministic per-column random generator.</summary>
    LCGRandom Rng { get; }
}
